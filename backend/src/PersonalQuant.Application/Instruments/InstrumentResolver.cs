using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IInstrumentResolver"/>.
/// </summary>
/// <param name="instruments">The instrument master.</param>
/// <param name="logger">Logger for resolution telemetry.</param>
internal sealed class InstrumentResolver(
    IInstrumentRepository instruments,
    ILogger<InstrumentResolver> logger) : IInstrumentResolver
{
    /// <inheritdoc />
    public async Task<InstrumentResolution> ResolveAsync(
        string? symbol,
        ExchangeCode? exchange = null,
        CancellationToken cancellationToken = default)
    {
        var folded = InstrumentSearchText.Normalise(symbol);

        // A string that is not a well-formed ticker cannot be one, so there is
        // nothing to look up. It is reported as not-found rather than as an
        // error: the caller asked whether a security answers to this, and the
        // answer is no.
        if (!Ticker.TryCreate(folded, out var ticker))
        {
            ApplicationLog.InstrumentResolved(
                logger, folded, InstrumentResolutionOutcome.NotFound, 0);

            return InstrumentResolution.NotFound(folded);
        }

        var candidates = await instruments
            .ListActiveByTickerAsync(ticker, cancellationToken)
            .ConfigureAwait(false);

        if (exchange is not null)
        {
            candidates = [.. candidates.Where(candidate => candidate.ExchangeCode == exchange)];
        }

        var resolution = candidates.Count switch
        {
            0 => InstrumentResolution.NotFound(ticker.Value),
            1 => InstrumentResolution.Resolved(ticker.Value, candidates[0]),
            _ => InstrumentResolution.Ambiguous(ticker.Value, candidates),
        };

        if (resolution.Outcome is InstrumentResolutionOutcome.Ambiguous)
        {
            // Worth a warning rather than a debug line: it means a caller that
            // assumed a symbol was unique has to be given an exchange, and
            // that is usually a gap in whatever produced the symbol.
            ApplicationLog.InstrumentSymbolAmbiguous(logger, ticker.Value, candidates.Count);
        }

        ApplicationLog.InstrumentResolved(
            logger, ticker.Value, resolution.Outcome, resolution.Candidates.Count);

        return resolution;
    }

    /// <inheritdoc />
    public Task<InstrumentSearchResult?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        id.IsEmpty
            ? Task.FromResult<InstrumentSearchResult?>(null)
            : instruments.FindResultByIdAsync(id, cancellationToken);
}
