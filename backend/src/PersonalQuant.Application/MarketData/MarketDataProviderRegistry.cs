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
    /// Finds the source to use when a caller did not name one.
    /// </summary>
    /// <remarks>
    /// Only meaningful when exactly one source is registered. With several,
    /// picking one would mean the same instruction ingested from different
    /// providers depending on registration order, and the bars would be
    /// attributed to whichever happened to win.
    /// </remarks>
    /// <param name="provider">The single registered source, when there is one.</param>
    /// <returns><see langword="true"/> when there is exactly one source.</returns>
    bool TryResolveDefault([NotNullWhen(true)] out IMarketDataProvider? provider);
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
    public bool TryResolveDefault([NotNullWhen(true)] out IMarketDataProvider? provider)
    {
        provider = Providers.Count == 1 ? Providers[0] : null;

        return provider is not null;
    }
}
