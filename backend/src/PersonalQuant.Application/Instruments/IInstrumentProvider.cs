using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// An external source of instrument reference data.
/// </summary>
/// <remarks>
/// <para>
/// The seam that lets the instrument master be populated from a real source
/// rather than from a seed file. Deliberately separate from
/// <see cref="MarketData.IMarketDataProvider"/>: one vendor may serve symbol
/// lists and not prices, or the reverse, and a single interface would force
/// every implementation to pretend it does both.
/// </para>
/// <para>
/// An implementation reads its own source in its own symbology and returns
/// what it found. It does not normalise symbols, decide what is a duplicate,
/// or create anything — those rules have to be identical across sources, and a
/// rule implemented once per provider is a rule that will eventually differ
/// between them.
/// </para>
/// </remarks>
public interface IInstrumentProvider
{
    /// <summary>
    /// Gets the code the aliases this source produces are attributed to.
    /// </summary>
    SourceCode Code { get; }

    /// <summary>
    /// Reads the source's full instrument list.
    /// </summary>
    /// <remarks>
    /// The whole list, not a delta. Symbol lists are small — a few thousand
    /// rows for an entire market — and a delta feed that misses a message
    /// leaves the master permanently short of a security with nothing to
    /// detect it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every instrument the source knows about.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    Task<IReadOnlyList<ProviderInstrument>> ListInstrumentsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One instrument exactly as a source reported it, before any rule has been
/// applied.
/// </summary>
/// <remarks>
/// <para>
/// Primitives, for the reason <see cref="MarketData.ProviderBar"/> is: a row
/// that fails validation has to survive long enough to be reported as
/// rejected, and a type that refuses to hold it would force every provider to
/// decide for itself what to do with bad data.
/// </para>
/// <para>
/// Almost every field is optional, which is not laziness — it is what symbol
/// lists actually look like. A vendor that publishes tickers and names but no
/// ISIN, no listing date and no asset class is the common case, and refusing
/// its rows would mean importing nothing.
/// </para>
/// </remarks>
/// <param name="Symbol">The source's spelling of the symbol, decoration included.</param>
/// <param name="Name">The security name.</param>
/// <param name="ExchangeCode">The venue, when the source states one.</param>
/// <param name="AssetType">The asset class, when the source states one.</param>
/// <param name="Currency">The quote currency, when the source states one.</param>
/// <param name="Isin">The ISIN, when the source carries one.</param>
/// <param name="Figi">The FIGI, when the source carries one.</param>
/// <param name="ListedOn">The first trading date, when the source carries one.</param>
public sealed record ProviderInstrument(
    string Symbol,
    string Name,
    string? ExchangeCode = null,
    string? AssetType = null,
    string? Currency = null,
    string? Isin = null,
    string? Figi = null,
    DateOnly? ListedOn = null);
