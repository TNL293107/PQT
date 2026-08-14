using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Turns a symbol, or a client-supplied identifier, into the canonical
/// instrument it refers to.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IInstrumentSearchService"/> and from any UI.
/// Commands, watchlists, alerts, charts, portfolio and the eventual research
/// agent all need to answer "which security is FPT?" without going near a
/// search box, and none of them should reimplement it.
/// </para>
/// <para>
/// Resolution is by ticker only, and deliberately so. It answers a question
/// with one correct answer; guessing from a company name would make the result
/// depend on what else happens to be listed, which is not something a command
/// or an alert can depend on. Free text belongs in search.
/// </para>
/// </remarks>
public interface IInstrumentResolver
{
    /// <summary>
    /// Resolves a symbol to the instrument currently trading under it.
    /// </summary>
    /// <remarks>
    /// Delisted instruments never resolve. Their tickers can already have been
    /// reissued to a different issuer, so resolving one would return whichever
    /// company held it most recently rather than the one the caller meant.
    /// </remarks>
    /// <param name="symbol">The ticker to resolve. Case and whitespace are normalised.</param>
    /// <param name="exchange">
    /// The venue to disambiguate with, when the caller knows it.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The outcome: resolved, not found, or ambiguous with its candidates.
    /// </returns>
    Task<InstrumentResolution> ResolveAsync(
        string? symbol,
        ExchangeCode? exchange = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an instrument by its canonical identifier.
    /// </summary>
    /// <remarks>
    /// The trusted path for an identifier that arrived from a client. The
    /// caller is asserting which record it wants, not what it contains, so
    /// every attribute is re-read here rather than taken from the request.
    /// </remarks>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when unknown.</returns>
    Task<InstrumentSearchResult?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default);
}
