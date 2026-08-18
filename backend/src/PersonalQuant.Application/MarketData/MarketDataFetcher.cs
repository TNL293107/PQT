using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Calls a provider under the ingestion policy: spaced, timed out, and
/// retried.
/// </summary>
/// <remarks>
/// Separated from the ingestion service so that "how we talk to a source" and
/// "what we do with what it says" are two things. The first is the same for
/// every provider and every instrument; the second is where the pipeline's
/// rules live.
/// </remarks>
public interface IMarketDataFetcher
{
    /// <summary>
    /// Fetches a request, retrying transient failures.
    /// </summary>
    /// <remarks>
    /// Does not throw for a provider failure. The pipeline has to record a
    /// failed run whatever happened, so the failure is returned as a value
    /// rather than as an exception the caller would have to catch to do its
    /// job.
    /// </remarks>
    /// <param name="provider">The source to read.</param>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The response, or the reason there is none.</returns>
    Task<FetchAttempt> FetchAsync(
        IMarketDataProvider provider,
        MarketDataRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of trying to read a provider, however many attempts it took.
/// </summary>
/// <param name="Result">The response, or <see langword="null"/> when every attempt failed.</param>
/// <param name="Attempts">How many calls were made.</param>
/// <param name="FailureReason">Why it failed, or <see langword="null"/> when it did not.</param>
public sealed record FetchAttempt(
    MarketDataFetchResult? Result,
    int Attempts,
    string? FailureReason)
{
    /// <summary>Gets a value indicating whether a response was obtained.</summary>
    public bool Succeeded => Result is not null;
}

/// <summary>
/// Default <see cref="IMarketDataFetcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Retries only what is worth retrying. A timeout or a provider that declared
/// its failure transient gets another attempt; a rejected request, an unknown
/// symbol or a malformed response does not, because repeating it produces the
/// same answer at three times the cost and delays the failed run being
/// recorded.
/// </para>
/// <para>
/// Cancellation is distinguished from a timeout. The two arrive as the same
/// exception type and mean opposite things — the caller gave up, or the
/// provider did — and reporting a shutdown as a provider failure would put
/// spurious failures in the audit table every time the process restarts.
/// </para>
/// </remarks>
/// <param name="policy">The retry, timeout and spacing settings.</param>
/// <param name="limiter">Enforces the gap between calls to one source.</param>
/// <param name="delays">Performs the waits.</param>
/// <param name="logger">Logger for retry telemetry.</param>
internal sealed class MarketDataFetcher(
    IngestionPolicy policy,
    IMarketDataCallLimiter limiter,
    IDelayScheduler delays,
    ILogger<MarketDataFetcher> logger) : IMarketDataFetcher
{
    /// <inheritdoc />
    public async Task<FetchAttempt> FetchAsync(
        IMarketDataProvider provider,
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        string? failureReason = null;

        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            var backoff = policy.BackoffBefore(attempt);

            if (backoff > TimeSpan.Zero)
            {
                ApplicationLog.MarketDataRetryScheduled(
                    logger,
                    provider.Code.Value,
                    request.Ticker.Value,
                    attempt,
                    (long)backoff.TotalMilliseconds,
                    failureReason ?? string.Empty);

                await delays.DelayAsync(backoff, cancellationToken).ConfigureAwait(false);
            }

            await limiter.WaitForTurnAsync(provider.Code, cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await CallAsync(provider, request, cancellationToken)
                    .ConfigureAwait(false);

                return new FetchAttempt(result, attempt, null);
            }
            catch (MarketDataProviderException exception)
            {
                failureReason = exception.Message;

                if (!exception.IsTransient)
                {
                    return new FetchAttempt(null, attempt, failureReason);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller is shutting down. Not a provider failure, and not
                // something to retry.
                throw;
            }
            catch (OperationCanceledException)
            {
                failureReason =
                    $"The provider did not answer within {policy.ProviderTimeout.TotalSeconds:0.#}s.";
            }
        }

        return new FetchAttempt(
            null,
            policy.MaxAttempts,
            failureReason ?? "The provider could not be read.");
    }

    private async Task<MarketDataFetchResult> CallAsync(
        IMarketDataProvider provider,
        MarketDataRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(policy.ProviderTimeout);

        return await provider
            .FetchBarsAsync(request, timeout.Token)
            .ConfigureAwait(false);
    }
}
