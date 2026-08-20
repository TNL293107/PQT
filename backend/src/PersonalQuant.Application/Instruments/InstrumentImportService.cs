using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IInstrumentImportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deduplication is tried in order of how much a match is worth trusting: the
/// provider's own symbol for this source, then a global identifier, then the
/// ticker on its venue. The order is the point. A ticker match is the weakest
/// of the three — tickers are reused after delisting and change on an exchange
/// transfer — so it is the last thing consulted, not the first.
/// </para>
/// <para>
/// When two of those routes disagree, the row is rejected rather than
/// resolved. An ISIN pointing at one instrument and a ticker at another means
/// either the master has a duplicate or the provider has a mistake, and
/// picking a side would merge two securities or split one.
/// </para>
/// <para>
/// The whole run commits once. A half-applied import leaves aliases pointing
/// at instruments that were never created, and the next run would then match
/// against them.
/// </para>
/// </remarks>
/// <param name="providers">Every registered instrument source.</param>
/// <param name="instruments">The instrument master.</param>
/// <param name="exchanges">The venue repository.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
/// <param name="logger">Logger for import telemetry.</param>
internal sealed class InstrumentImportService(
    IEnumerable<IInstrumentProvider> providers,
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<InstrumentImportService> logger) : IInstrumentImportService
{
    /// <inheritdoc />
    public async Task<InstrumentImportReport> ImportAsync(
        SourceCode? source,
        CancellationToken cancellationToken = default)
    {
        var provider = Resolve(source);

        var rows = await provider
            .ListInstrumentsAsync(cancellationToken)
            .ConfigureAwait(false);

        var run = new ImportRun(provider.Code, clock.UtcNow);

        foreach (var row in rows)
        {
            await ApplyAsync(run, row, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var report = run.ToReport(rows.Count);

        ApplicationLog.InstrumentsImported(
            logger,
            report.Source,
            report.RowsRead,
            report.Created,
            report.Matched,
            report.Enriched,
            report.Rejected);

        LogRejections(report);

        return report;
    }

    private IInstrumentProvider Resolve(SourceCode? source)
    {
        var registered = providers.ToList();

        if (source is null)
        {
            // Only meaningful with exactly one source. With several, picking
            // one would mean the same instruction importing from different
            // providers depending on registration order.
            return registered.Count == 1
                ? registered[0]
                : throw new InvalidOperationException(
                    registered.Count == 0
                        ? "No instrument source is registered."
                        : "Several instrument sources are registered, so one must be named.");
        }

        return registered.Find(candidate => candidate.Code == source)
            ?? throw new InvalidOperationException(
                $"No instrument source is registered under the code '{source}'.");
    }

    private async Task ApplyAsync(
        ImportRun run,
        ProviderInstrument row,
        CancellationToken cancellationToken)
    {
        if (!ProviderSymbol.TryParse(row.Symbol, out var symbol, out var symbolProblem))
        {
            run.Reject(row, InstrumentImportRejectionReason.UnreadableSymbol, symbolProblem);
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            run.Reject(
                row,
                InstrumentImportRejectionReason.UnusableName,
                $"'{symbol.Ticker}' arrived with no security name.");
            return;
        }

        if (!run.FirstSightOf(symbol.Raw))
        {
            run.Reject(
                row,
                InstrumentImportRejectionReason.DuplicateWithinImport,
                $"'{symbol.Raw}' appeared more than once in one import.");
            return;
        }

        if (!TryReadIdentifiers(row, symbol, out var globals, out var identifierProblem))
        {
            run.Reject(row, InstrumentImportRejectionReason.InvalidIdentifier, identifierProblem);
            return;
        }

        var exchangeId = await ResolveExchangeAsync(run, row, symbol, cancellationToken)
            .ConfigureAwait(false);

        if (exchangeId is null)
        {
            return;
        }

        var match = await MatchAsync(run, provider: run.Source, symbol, globals, exchangeId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (match.Conflicted)
        {
            run.Reject(row, InstrumentImportRejectionReason.ConflictingIdentity, match.Detail!);
            return;
        }

        if (match.Instrument is { } existing)
        {
            Enrich(run, existing, symbol, globals);
            return;
        }

        Create(run, row, symbol, globals, exchangeId.Value);
    }

    private static bool TryReadIdentifiers(
        ProviderInstrument row,
        ProviderSymbol symbol,
        out List<IdentifierValue> globals,
        out string problem)
    {
        globals = [];

        if (!TryRead(IdentifierScheme.Isin, row.Isin, globals, out problem)
            || !TryRead(IdentifierScheme.Figi, row.Figi, globals, out problem))
        {
            problem = $"{symbol.Ticker}: {problem}";
            return false;
        }

        problem = string.Empty;
        return true;

        static bool TryRead(
            IdentifierScheme scheme,
            string? value,
            List<IdentifierValue> into,
            out string problem)
        {
            problem = string.Empty;

            // Absent is normal. Most symbol lists carry neither an ISIN nor a
            // FIGI, and refusing those rows would mean importing nothing.
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            // Declared separately rather than passed straight through: the
            // out parameter here is non-nullable, and TryCreate's is not.
            if (!IdentifierValue.TryCreate(scheme, value, out var identifier, out var failure))
            {
                problem = failure;
                return false;
            }

            into.Add(identifier);
            return true;
        }
    }

    private async Task<ExchangeId?> ResolveExchangeAsync(
        ImportRun run,
        ProviderInstrument row,
        ProviderSymbol symbol,
        CancellationToken cancellationToken)
    {
        // An explicitly stated venue beats one inferred from the symbol's
        // decoration. A vendor's suffix can lag an exchange transfer by
        // months, and the row's own field is the more considered answer.
        var candidate = row.ExchangeCode is { } stated && ExchangeCode.TryCreate(stated, out var parsed)
            ? parsed
            : symbol.VenueHint;

        if (candidate is null)
        {
            run.Reject(
                row,
                InstrumentImportRejectionReason.UnknownExchange,
                $"'{symbol.Raw}' names no venue, and its symbol carries no recognised one.");
            return null;
        }

        var exchangeId = await run
            .ResolveExchangeAsync(candidate, exchanges, cancellationToken)
            .ConfigureAwait(false);

        if (exchangeId is null)
        {
            run.Reject(
                row,
                InstrumentImportRejectionReason.UnknownExchange,
                $"'{candidate}' is not a venue this system holds. Seed the exchange first.");
        }

        return exchangeId;
    }

    private async Task<MatchOutcome> MatchAsync(
        ImportRun run,
        SourceCode provider,
        ProviderSymbol symbol,
        List<IdentifierValue> globals,
        ExchangeId exchangeId,
        CancellationToken cancellationToken)
    {
        var providerSymbol = IdentifierValue.Create(IdentifierScheme.ProviderSymbol, symbol.Raw);

        // Strongest first: this provider has told us before which instrument
        // it means by this exact spelling.
        var bySymbol = await run
            .FindByIdentifierAsync(providerSymbol, provider, instruments, cancellationToken)
            .ConfigureAwait(false);

        InstrumentId? resolved = bySymbol;
        string? via = bySymbol is null ? null : $"the provider symbol {symbol.Raw}";

        foreach (var global in globals)
        {
            var byGlobal = await run
                .FindByIdentifierAsync(global, source: null, instruments, cancellationToken)
                .ConfigureAwait(false);

            if (byGlobal is null)
            {
                continue;
            }

            if (resolved is { } already && already != byGlobal)
            {
                return MatchOutcome.Conflict(
                    $"{global} names a different instrument than {via} does.");
            }

            resolved ??= byGlobal;
            via ??= global.ToString();
        }

        if (resolved is not null)
        {
            return MatchOutcome.Matched(resolved.Value);
        }

        // Weakest, and therefore last: a ticker is reused after delisting and
        // changes on an exchange transfer, so it identifies a listing rather
        // than a security.
        var byTicker = await run
            .FindActiveByTickerAsync(exchangeId, symbol.Ticker, instruments, cancellationToken)
            .ConfigureAwait(false);

        return byTicker is null ? MatchOutcome.None : MatchOutcome.Matched(byTicker.Value);
    }

    /// <summary>
    /// Attaches whatever the row taught us to an instrument already held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enrichment is additive and limited to aliases. The row's name, asset
    /// class and listing date are deliberately <em>not</em> written over what
    /// the master already holds — a provider's spelling of a company name
    /// differs from the registered one, its asset classification is its own,
    /// and a listing date it invented would overwrite a sourced one. The
    /// seeder takes the same position for the same reason.
    /// </para>
    /// <para>
    /// The consequence is that correcting a record is a deliberate act, not
    /// something the next nightly import does silently. That is the intended
    /// trade: this is the system of record.
    /// </para>
    /// </remarks>
    private void Enrich(
        ImportRun run,
        InstrumentId instrumentId,
        ProviderSymbol symbol,
        List<IdentifierValue> globals)
    {
        var aliases = RecordAliases(run, instrumentId, symbol, globals);

        // "Enriched" means the master gained a fact it did not have. A row
        // that only confirmed what was already known is matched, not enriched,
        // and the difference is what makes a steady state visible.
        if (aliases > 0)
        {
            run.Enriched();
        }
        else
        {
            run.Matched();
        }
    }

    private void Create(
        ImportRun run,
        ProviderInstrument row,
        ProviderSymbol symbol,
        List<IdentifierValue> globals,
        ExchangeId exchangeId)
    {
        var currency = row.Currency is { } stated && CurrencyCode.TryCreate(stated, out var parsed)
            ? parsed
            // Vietnam-first, per ADR-008. A source that states nothing is
            // describing a Vietnamese venue, and every one of them quotes in
            // dong.
            : CurrencyCode.Vnd;

        var instrument = Instrument.Register(
            exchangeId,
            symbol.Ticker,
            row.Name,
            ParseAssetType(row.AssetType),
            currency,
            run.OccurredAtUtc);

        // A symbol list describes securities that trade. The listing date is
        // recorded when the source carries one and left empty otherwise,
        // rather than invented.
        if (row.ListedOn is { } listedOn)
        {
            instrument.List(listedOn, run.OccurredAtUtc);
        }
        else
        {
            instrument.List(run.OccurredAtUtc);
        }

        instruments.Add(instrument);
        run.Created(exchangeId, symbol.Ticker, instrument.Id);

        RecordAliases(run, instrument.Id, symbol, globals);
    }

    private int RecordAliases(
        ImportRun run,
        InstrumentId instrumentId,
        ProviderSymbol symbol,
        List<IdentifierValue> globals)
    {
        var recorded = 0;

        var providerSymbol = IdentifierValue.Create(IdentifierScheme.ProviderSymbol, symbol.Raw);

        if (run.TryClaim(providerSymbol, run.Source, instrumentId))
        {
            instruments.AddIdentifier(InstrumentIdentifier.Record(
                instrumentId, providerSymbol, run.Source, run.OccurredAtUtc));
            recorded++;
        }

        foreach (var global in globals)
        {
            if (!run.TryClaim(global, source: null, instrumentId))
            {
                continue;
            }

            instruments.AddIdentifier(InstrumentIdentifier.Record(
                instrumentId, global, source: null, run.OccurredAtUtc));
            recorded++;
        }

        run.AliasesRecorded(recorded);
        return recorded;
    }

    private static AssetType ParseAssetType(string? value) =>
        Enum.TryParse<AssetType>(value, ignoreCase: true, out var parsed) && parsed != AssetType.Unspecified
            ? parsed
            // Unspecified rather than a guess. An instrument whose class is
            // unknown can be reclassified later; one guessed wrong is filtered
            // into the wrong universe and nothing reports it.
            : AssetType.Unspecified;

    private void LogRejections(InstrumentImportReport report)
    {
        if (report.Rejections.Count == 0 || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        // Grouped by reason. A source with no venue column rejects every row
        // it has, and one line saying so is readable where four thousand are
        // not.
        foreach (var group in report.Rejections.GroupBy(rejection => rejection.Reason))
        {
            ApplicationLog.InstrumentImportRowsRejected(
                logger, report.Source, group.Key, group.Count(), group.First().Detail);
        }
    }

    /// <summary>The outcome of trying to match one provider row.</summary>
    private readonly record struct MatchOutcome(InstrumentId? Instrument, string? Detail)
    {
        public static MatchOutcome None => new(null, null);

        public bool Conflicted => Instrument is null && Detail is not null;

        public static MatchOutcome Matched(InstrumentId instrument) => new(instrument, null);

        public static MatchOutcome Conflict(string detail) => new(null, detail);
    }
}
