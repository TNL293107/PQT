using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// One external source of market data.
/// </summary>
/// <remarks>
/// <para>
/// The seam that keeps the rest of the system free of any particular vendor.
/// Nothing above this interface knows whether the data arrived over HTTP, from
/// a file, or from a broker session, and adding a source is implementing this
/// and registering it — not editing the pipeline.
/// </para>
/// <para>
/// An implementation does two things and no more: address its own API in its
/// own symbology, and return what came back. It does <em>not</em> validate,
/// deduplicate, decide what to store, or advance a checkpoint. Those rules
/// have to be identical across every source, and a rule implemented once per
/// provider is a rule that will eventually differ between them.
/// </para>
/// <para>
/// It also does not retry or rate-limit itself. Both are applied around it by
/// the pipeline, so that the policy is one thing to configure and one thing to
/// test rather than a per-vendor reimplementation.
/// </para>
/// </remarks>
public interface IMarketDataProvider
{
    /// <summary>
    /// Gets the code this source is recorded under on every bar it produces.
    /// </summary>
    SourceCode Code { get; }

    /// <summary>
    /// Gets what this source declares it can serve.
    /// </summary>
    /// <remarks>
    /// Declared rather than discovered by failure. A provider that only has
    /// end-of-day data should cause an intraday request to be skipped with a
    /// reason, not to fail after three retries against an endpoint that was
    /// never going to answer — and the same holds for a venue it does not
    /// cover, an asset type it does not serve, and history it does not hold.
    /// </remarks>
    ProviderCapability Capability { get; }

    /// <summary>
    /// Gets the resolutions this source can serve.
    /// </summary>
    /// <remarks>
    /// Kept, and derived. It is the dimension the pipeline reads most often,
    /// and one delegating property is cheaper than a rename that ripples
    /// through the ingestion service, its tests and every provider.
    /// </remarks>
    IReadOnlySet<BarInterval> SupportedIntervals => Capability.Intervals;

    /// <summary>
    /// Fetches the bars covering a request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// May return fewer bars than the range implies, and routinely will: the
    /// market is closed at weekends and on public holidays, and a security may
    /// not have traded. An empty result is a legitimate answer, not an error.
    /// </para>
    /// <para>
    /// Implementations throw <see cref="MarketDataProviderException"/> for a
    /// failure the pipeline may sensibly retry, and let anything else
    /// propagate.
    /// </para>
    /// </remarks>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The provider's response and the rows parsed from it.</returns>
    /// <exception cref="MarketDataProviderException">The source could not be read.</exception>
    Task<MarketDataFetchResult> FetchBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A failure reading an external market data source.
/// </summary>
/// <remarks>
/// <para>
/// A distinct type so the pipeline can tell "the source did not answer" from
/// "this code has a bug". The first is expected, is retried, and ends as a
/// recorded failed run; the second must not be swallowed into an audit row
/// that makes a defect look like a flaky provider.
/// </para>
/// <para>
/// <see cref="IsTransient"/> separates a timeout or a rate-limit response,
/// which retrying will fix, from a rejected request or an unknown symbol,
/// which retrying will only repeat more expensively.
/// </para>
/// </remarks>
public sealed class MarketDataProviderException : Exception
{
    /// <summary>Creates an exception with no detail.</summary>
    public MarketDataProviderException()
        : this("A market data provider could not be read.")
    {
    }

    /// <summary>Creates a non-transient failure.</summary>
    /// <param name="message">What went wrong.</param>
    public MarketDataProviderException(string message)
        : base(message) => IsTransient = false;

    /// <summary>Creates a failure wrapping an underlying error.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public MarketDataProviderException(string message, Exception innerException)
        : base(message, innerException) => IsTransient = false;

    /// <summary>Creates a failure and states whether retrying could help.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="isTransient">Whether the same call could succeed later.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public MarketDataProviderException(
        string message,
        bool isTransient,
        Exception? innerException = null)
        : base(message, innerException) => IsTransient = isTransient;

    /// <summary>
    /// Gets a value indicating whether the same call could succeed if repeated.
    /// </summary>
    public bool IsTransient { get; }
}
