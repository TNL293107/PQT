using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// The market data sources this deployment has.
/// </summary>
/// <remarks>
/// Providers are registered rather than referenced by name from the pipeline.
/// That is what lets a deployment run with a file-based source, a vendor feed,
/// or both, without the ingestion code knowing which — the roadmap's
/// requirement that no provider is hard-coded, expressed as a lookup.
/// </remarks>
public interface IMarketDataProviderRegistry
{
    /// <summary>Gets every registered source, ordered by code.</summary>
    IReadOnlyList<IMarketDataProvider> Providers { get; }

    /// <summary>
    /// Finds a source by its code.
    /// </summary>
    /// <param name="code">The code to look up.</param>
    /// <param name="provider">The source when it is registered.</param>
    /// <returns><see langword="true"/> when the source exists.</returns>
    bool TryResolve(SourceCode code, [NotNullWhen(true)] out IMarketDataProvider? provider);

    /// <summary>
    /// Chooses the one source that can serve a request, or says why none was
    /// chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is <em>exactly one registered source can serve this request</em>,
    /// not <em>exactly one source is registered</em>. A deployment holding a
    /// daily Vietnamese feed and an intraday-only feed has no ambiguity about a
    /// daily request: only one candidate can answer it.
    /// </para>
    /// <para>
    /// Ambiguity stays an error. There is no tie-break, no priority order and
    /// no fallback to a second source when the first cannot answer — a mixed
    /// series is made visible, never assembled silently.
    /// </para>
    /// </remarks>
    /// <param name="criteria">What the caller knows about the data it wants.</param>
    /// <returns>The chosen source, or the specific reason there is none.</returns>
    ProviderSelection SelectProvider(ProviderCriteria criteria);
}

/// <summary>
/// Default <see cref="IMarketDataProviderRegistry"/>, built from whatever
/// providers dependency injection was given.
/// </summary>
/// <remarks>
/// Duplicate codes are a composition error and are rejected at construction.
/// Two sources answering to one code would make a bar's recorded origin
/// ambiguous, which defeats the reason the code is stored at all.
/// </remarks>
/// <param name="providers">Every registered source.</param>
internal sealed class MarketDataProviderRegistry : IMarketDataProviderRegistry
{
    private readonly Dictionary<string, IMarketDataProvider> _byCode;

    public MarketDataProviderRegistry(IEnumerable<IMarketDataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _byCode = new Dictionary<string, IMarketDataProvider>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (!_byCode.TryAdd(provider.Code.Value, provider))
            {
                throw new InvalidOperationException(
                    $"Two market data providers are registered under the code '{provider.Code}'.");
            }

            // A source declaring nothing it can serve would skip every run it
            // was ever given, at midnight, silently. Composition is the place
            // to find that out.
            provider.Capability.Validate(provider.Code);
        }

        Providers = [.. _byCode.Values.OrderBy(provider => provider.Code.Value, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<IMarketDataProvider> Providers { get; }

    /// <inheritdoc />
    public bool TryResolve(SourceCode code, [NotNullWhen(true)] out IMarketDataProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(code);

        return _byCode.TryGetValue(code.Value, out provider);
    }

    /// <inheritdoc />
    public ProviderSelection SelectProvider(ProviderCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.Source is { } named)
        {
            return TryResolve(named, out var provider)
                ? Qualify(provider, criteria)
                : ProviderSelection.Refuse(
                    ProviderSelectionOutcome.Unknown,
                    $"No market data source is registered under the code '{named}'.");
        }

        var candidates = Providers.Where(provider => CanServe(provider, criteria)).ToList();

        return candidates.Count switch
        {
            1 => ProviderSelection.Select(candidates[0]),

            // One registered source that cannot serve it is qualified rather
            // than counted, so the reason names the dimension that failed
            // instead of reporting that nothing matched.
            0 when Providers.Count == 1 => Qualify(Providers[0], criteria),

            0 => ProviderSelection.Refuse(
                ProviderSelectionOutcome.None,
                Providers.Count == 0
                    ? "No market data source is registered."
                    : $"No registered market data source serves {Describe(criteria)}. Registered: "
                        + $"{string.Join(", ", Providers.Select(provider => provider.Code.Value))}."),

            // Named rather than chosen. Two sources that can both answer are
            // two answers to one question, and registration order is not a
            // reason to prefer either.
            _ => ProviderSelection.Refuse(
                ProviderSelectionOutcome.Ambiguous,
                $"Several market data sources serve {Describe(criteria)} and none was named: "
                    + $"{string.Join(", ", candidates.Select(provider => provider.Code.Value))}."),
        };
    }

    /// <summary>
    /// Explains why a named source cannot serve a request, naming the
    /// dimension that failed.
    /// </summary>
    private static ProviderSelection Qualify(
        IMarketDataProvider provider,
        ProviderCriteria criteria)
    {
        var capability = provider.Capability;

        if (!capability.Serves(criteria.Interval))
        {
            return ProviderSelection.Refuse(
                ProviderSelectionOutcome.Incapable,
                $"'{provider.Code}' does not serve {criteria.Interval} bars.");
        }

        if (!capability.Covers(criteria.Exchange))
        {
            return ProviderSelection.Refuse(
                ProviderSelectionOutcome.Incapable,
                $"'{provider.Code}' does not cover {criteria.Exchange}.");
        }

        if (!capability.Serves(criteria.AssetType))
        {
            return ProviderSelection.Refuse(
                ProviderSelectionOutcome.Incapable,
                $"'{provider.Code}' does not serve {criteria.AssetType} instruments.");
        }

        return ProviderSelection.Select(provider);
    }

    private static bool CanServe(IMarketDataProvider provider, ProviderCriteria criteria) =>
        provider.Capability.Serves(criteria.Interval)
        && provider.Capability.Covers(criteria.Exchange)
        && provider.Capability.Serves(criteria.AssetType);

    private static string Describe(ProviderCriteria criteria)
    {
        var parts = new List<string> { $"{criteria.Interval} bars" };

        if (criteria.Exchange is { } exchange)
        {
            parts.Add($"on {exchange}");
        }

        if (criteria.AssetType is { } assetType)
        {
            parts.Add($"for {assetType} instruments");
        }

        return string.Join(" ", parts);
    }
}
