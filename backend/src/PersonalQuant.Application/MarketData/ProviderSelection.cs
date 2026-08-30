using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// What a caller knows about the data it wants, for choosing a source.
/// </summary>
/// <remarks>
/// The venue and the asset type are nullable because selection happens for
/// instructions naming an instrument this system does not hold. An unknown
/// dimension cannot refuse a provider — it is not evidence that the provider
/// fails to cover it — so it is simply not tested.
/// </remarks>
/// <param name="Interval">The resolution wanted.</param>
/// <param name="Source">The source the caller named, or null to let selection decide.</param>
/// <param name="Exchange">The venue, when the instrument is known.</param>
/// <param name="AssetType">The asset type, when the instrument is known.</param>
public sealed record ProviderCriteria(
    BarInterval Interval,
    SourceCode? Source = null,
    ExchangeCode? Exchange = null,
    AssetType? AssetType = null);

/// <summary>How a selection ended.</summary>
public enum ProviderSelectionOutcome
{
    /// <summary>One source was chosen.</summary>
    Selected = 1,

    /// <summary>The caller named a source that is not registered.</summary>
    Unknown = 2,

    /// <summary>
    /// Several registered sources could serve the request and none was named.
    /// </summary>
    /// <remarks>
    /// An error, deliberately. Two sources that could both answer are two
    /// answers to one question, and picking between them by registration order
    /// would attribute a series to whichever was composed first.
    /// </remarks>
    Ambiguous = 3,

    /// <summary>
    /// The named source is registered and cannot serve this request.
    /// </summary>
    Incapable = 4,

    /// <summary>Nothing registered can serve the request.</summary>
    None = 5,
}

/// <summary>
/// The source chosen for a request, or why none was.
/// </summary>
/// <remarks>
/// <para>
/// The reason is not decoration. It lands in <see cref="IngestionRun"/>'s skip
/// text, which is the record that explains a gap in a series, and a vague
/// reason there is a gap nobody can close. Every unsuccessful outcome names the
/// dimension that failed and the value that was asked for.
/// </para>
/// <para>
/// <strong>There is no fallback.</strong> Nothing here tries a second source
/// when the first cannot answer, and nothing ranks candidates. Falling through
/// would assemble one series from two symbologies, two adjustment conventions
/// and two restatement policies, and every consumer that reads a series rather
/// than a row would inherit the mixture with no way to notice. The mixture is
/// made visible; it is not made easy.
/// </para>
/// </remarks>
public sealed record ProviderSelection
{
    private ProviderSelection(
        IMarketDataProvider? provider,
        ProviderSelectionOutcome outcome,
        string? reason)
    {
        Provider = provider;
        Outcome = outcome;
        Reason = reason;
    }

    /// <summary>Gets the chosen source, when one was chosen.</summary>
    public IMarketDataProvider? Provider { get; }

    /// <summary>Gets how the selection ended.</summary>
    public ProviderSelectionOutcome Outcome { get; }

    /// <summary>Gets a caller-safe explanation, when nothing was chosen.</summary>
    public string? Reason { get; }

    /// <summary>Gets a value indicating whether a source was chosen.</summary>
    public bool IsSelected => Provider is not null;

    /// <summary>Records a successful selection.</summary>
    /// <param name="provider">The chosen source.</param>
    /// <returns>The selection.</returns>
    public static ProviderSelection Select(IMarketDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new ProviderSelection(provider, ProviderSelectionOutcome.Selected, reason: null);
    }

    /// <summary>Records that no source was chosen.</summary>
    /// <param name="outcome">Why not.</param>
    /// <param name="reason">The specific explanation, naming what failed.</param>
    /// <returns>The selection.</returns>
    public static ProviderSelection Refuse(ProviderSelectionOutcome outcome, string reason) =>
        new(provider: null, outcome, reason);
}
