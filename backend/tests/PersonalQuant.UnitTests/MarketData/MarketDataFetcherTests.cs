using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the retry, timeout and spacing policy applied around a provider.
/// </summary>
/// <remarks>
/// Every wait goes through an injected scheduler, so the retry ladder is
/// asserted in milliseconds rather than by actually sleeping for it.
/// </remarks>
public sealed class MarketDataFetcherTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_provider_that_answers_first_time_is_called_once()
    {
        var provider = new StubProvider(_ => Task.FromResult(Response()));
        var fetcher = CreateFetcher(out var delays, out _);

        // Act
        var attempt = await fetcher.FetchAsync(
            provider, Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(attempt.Succeeded);
        Assert.Equal(1, attempt.Attempts);
        Assert.Equal(1, provider.CallCount);
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_with_growing_backoff()
    {
        // Two failures then a success: the second attempt waits the initial
        // backoff, the third waits it multiplied.
        var attempts = 0;
        var provider = new StubProvider(_ =>
            ++attempts < 3
                ? Task.FromException<MarketDataFetchResult>(
                    new MarketDataProviderException("temporarily unavailable", isTransient: true))
                : Task.FromResult(Response()));

        var fetcher = CreateFetcher(out var delays, out _);

        // Act
        var attempt = await fetcher.FetchAsync(
            provider, Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(attempt.Succeeded);
        Assert.Equal(3, attempt.Attempts);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)],
            delays.Waits);
    }

    [Fact]
    public async Task A_non_transient_failure_is_not_retried()
    {
        // Repeating a rejected request produces the same answer at three times
        // the cost and delays the failed run being recorded.
        var provider = new StubProvider(_ => Task.FromException<MarketDataFetchResult>(
            new MarketDataProviderException("unknown symbol")));

        var fetcher = CreateFetcher(out var delays, out _);

        // Act
        var attempt = await fetcher.FetchAsync(
            provider, Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(attempt.Succeeded);
        Assert.Equal(1, attempt.Attempts);
        Assert.Equal("unknown symbol", attempt.FailureReason);
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task Every_attempt_failing_returns_the_last_reason()
    {
        var provider = new StubProvider(_ => Task.FromException<MarketDataFetchResult>(
            new MarketDataProviderException("still down", isTransient: true)));

        var fetcher = CreateFetcher(out _, out _);

        // Act
        var attempt = await fetcher.FetchAsync(
            provider, Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(attempt.Succeeded);
        Assert.Equal(3, attempt.Attempts);
        Assert.Equal(3, provider.CallCount);
        Assert.Equal("still down", attempt.FailureReason);
    }

    [Fact]
    public async Task A_provider_that_never_answers_times_out_and_is_retried()
    {
        // The failure this exists to prevent: a source that accepts the call
        // and then hangs, holding the run open while everything reports
        // healthy.
        var provider = new StubProvider(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response();
        });

        var fetcher = CreateFetcher(
            out _,
            out _,
            policy => policy with { ProviderTimeout = TimeSpan.FromMilliseconds(50) });

        // Act
        var attempt = await fetcher.FetchAsync(
            provider, Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(attempt.Succeeded);
        Assert.Equal(3, attempt.Attempts);
        Assert.Contains("did not answer", attempt.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_the_caller_is_not_reported_as_a_provider_failure()
    {
        // A shutdown must not fill the audit table with spurious failures.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var provider = new StubProvider(token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response());
        });

        var fetcher = CreateFetcher(out _, out _);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.FetchAsync(provider, Request(), cancellation.Token));
    }

    [Fact]
    public async Task Every_call_passes_through_the_rate_limiter()
    {
        var provider = new StubProvider(_ => Task.FromException<MarketDataFetchResult>(
            new MarketDataProviderException("down", isTransient: true)));

        var fetcher = CreateFetcher(out _, out var limiter);

        await fetcher.FetchAsync(provider, Request(), TestContext.Current.CancellationToken);

        Assert.Equal(3, limiter.Waits);
    }

    private static MarketDataFetcher CreateFetcher(
        out RecordingDelayScheduler delays,
        out CountingLimiter limiter,
        Func<IngestionPolicy, IngestionPolicy>? configure = null)
    {
        var policy = new IngestionPolicy
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 2.0,
            MaxBackoff = TimeSpan.FromSeconds(1),
            ProviderTimeout = TimeSpan.FromSeconds(5),
            MinimumCallSpacing = TimeSpan.Zero,
        };

        policy = (configure?.Invoke(policy) ?? policy).Validated();

        delays = new RecordingDelayScheduler();
        limiter = new CountingLimiter();

        return new MarketDataFetcher(
            policy, limiter, delays, NullLogger<MarketDataFetcher>.Instance);
    }

    private static MarketDataFetchResult Response() =>
        new("payload", "text/csv", [new ProviderBar(From, 1m, 1m, 1m, 1m, 0, null)]);

    private static MarketDataRequest Request()
    {
        Assert.True(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("FPT"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            From,
            To,
            out var request,
            out var problem),
            problem);

        return request;
    }

    /// <summary>A provider whose behaviour each test supplies.</summary>
    /// <remarks>
    /// One asynchronous constructor rather than an overload pair: a lambda
    /// that returns a value and one that returns a task are equally applicable
    /// to both, and the compiler cannot choose between them.
    /// </remarks>
    private sealed class StubProvider(Func<CancellationToken, Task<MarketDataFetchResult>> behaviour)
        : IMarketDataProvider
    {
        public int CallCount { get; private set; }

        public SourceCode Code { get; } = SourceCode.Create("STUB");

        public ProviderCapability Capability { get; } = TestCapability.For(
            SourceCode.Create("STUB"),
            new HashSet<BarInterval> { BarInterval.OneDay });

        public Task<MarketDataFetchResult> FetchBarsAsync(
            MarketDataRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            return behaviour(cancellationToken);
        }
    }

    /// <summary>Records what it was asked to wait for and returns at once.</summary>
    private sealed class RecordingDelayScheduler : IDelayScheduler
    {
        public List<TimeSpan> Waits { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Waits.Add(duration);

            return Task.CompletedTask;
        }
    }

    private sealed class CountingLimiter : IMarketDataCallLimiter
    {
        public int Waits { get; private set; }

        public Task WaitForTurnAsync(
            SourceCode source,
            CancellationToken cancellationToken = default)
        {
            Waits++;

            return Task.CompletedTask;
        }
    }
}
