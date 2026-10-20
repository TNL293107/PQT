using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>Imports disclosed financial reports from one named provider.</summary>
public interface IFundamentalsImportService
{
    /// <summary>Gets the codes of the providers an import can name.</summary>
    IReadOnlyList<SourceCode> Providers { get; }

    /// <summary>Reads every filing the provider holds and records what is new or changed.</summary>
    /// <param name="provider">The provider to read; nothing else is consulted.</param>
    /// <param name="tickers">Only these tickers, or every ticker when empty.</param>
    /// <param name="cancellationToken">Cancels the import between filings.</param>
    /// <returns>What was recorded and what was refused.</returns>
    /// <exception cref="ArgumentException">No provider has that code.</exception>
    Task<FundamentalsImportReport> ImportAsync(
        SourceCode provider,
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default);
}

/// <summary>What an import recorded and refused.</summary>
/// <param name="Provider">The provider read.</param>
/// <param name="FilingsRead">Filings the provider supplied, after the ticker filter.</param>
/// <param name="ReportsRecorded">New reports recorded with values.</param>
/// <param name="ReportsWithoutValues">New reports recorded as disclosed without values.</param>
/// <param name="ReportsAlreadyHeld">Filings already recorded, read again.</param>
/// <param name="LinesRecorded">New lines recorded.</param>
/// <param name="LinesRestated">Lines whose value changed since they were last read.</param>
/// <param name="Late">Filings recorded after their deadline.</param>
/// <param name="Rejections">Filings refused, with the reason.</param>
/// <param name="Withheld">Filings recorded with their values withheld, because their form is another filing's.</param>
/// <param name="Dropped">Lines dropped as implausible from filings otherwise read.</param>
public sealed record FundamentalsImportReport(
    SourceCode Provider,
    int FilingsRead,
    int ReportsRecorded,
    int ReportsWithoutValues,
    int ReportsAlreadyHeld,
    int LinesRecorded,
    int LinesRestated,
    IReadOnlyList<LateFiling> Late,
    IReadOnlyList<FilingRejection> Rejections,
    IReadOnlyList<FilingRejection> Withheld,
    IReadOnlyList<FilingRejection> Dropped);

/// <summary>
/// Records disclosed financial reports and the values they state (ADR-029,
/// sprint 6 item 6.3).
/// </summary>
/// <remarks>
/// <para>
/// Each filing is classified, normalised and checked before anything is
/// staged, and saved on its own, so a refused filing never leaves half a
/// report behind and one bad filing does not stop the rest.
/// </para>
/// <para>
/// Append-only. A filing already recorded is read again for changed values:
/// a changed value supersedes its reading (<see cref="StatementLine.Restate"/>),
/// a new line is added, and nothing is removed. The report itself, its date
/// above all, never changes. The raw payload is retained whenever its
/// checksum is new.
/// </para>
/// </remarks>
/// <param name="providers">The registered providers.</param>
/// <param name="instruments">Resolves a filing's ticker.</param>
/// <param name="exchanges">Gives the venue's time zone, which turns a filing date into an instant.</param>
/// <param name="fundamentals">Reports, lines and payloads.</param>
/// <param name="unitOfWork">Saves each filing.</param>
/// <param name="clock">Stamps what is recorded.</param>
/// <param name="logger">Receives the run's outcome.</param>
public sealed class FundamentalsImportService(
    IEnumerable<IFundamentalsProvider> providers,
    IInstrumentResolver instruments,
    IExchangeRepository exchanges,
    IFundamentalsRepository fundamentals,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<FundamentalsImportService> logger) : IFundamentalsImportService
{
    private readonly IReadOnlyList<IFundamentalsProvider> _providers = [.. providers];

    /// <inheritdoc />
    public IReadOnlyList<SourceCode> Providers => [.. _providers.Select(provider => provider.Code)];

    /// <inheritdoc />
    public async Task<FundamentalsImportReport> ImportAsync(
        SourceCode provider,
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(tickers);

        var source = _providers.FirstOrDefault(candidate => candidate.Code == provider)
            ?? throw new ArgumentException($"No fundamentals provider is registered as '{provider.Value}'.", nameof(provider));

        var wanted = tickers.Select(ticker => ticker.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var filings = (await source.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(filing => wanted.Count == 0 || wanted.Contains(filing.Ticker.Trim().ToUpperInvariant()))
            .OrderBy(filing => filing.SubmittedOn)
            .ThenBy(filing => filing.Reference, StringComparer.Ordinal)
            .ToList();

        var run = new Run(source);
        var issuers = new Dictionary<string, Issuer?>(StringComparer.Ordinal);
        var read = filings.ToDictionary(filing => filing, Read);
        var withheld = SharedForms.Find(read);

        foreach (var filing in filings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ticker = filing.Ticker.Trim().ToUpperInvariant();

            if (!issuers.TryGetValue(ticker, out var issuer))
            {
                issuer = await ResolveAsync(ticker, cancellationToken).ConfigureAwait(false);
                issuers[ticker] = issuer;
            }

            if (issuer is null)
            {
                run.Rejections.Add(new FilingRejection(
                    filing.Reference, filing.Ticker, FilingRejectionReason.UnknownInstrument, $"'{filing.Ticker}' is not an active instrument."));
                continue;
            }

            await ImportAsync(run, filing, read[filing], withheld.GetValueOrDefault(filing), issuer, cancellationToken)
                .ConfigureAwait(false);
        }

        var report = new FundamentalsImportReport(
            source.Code,
            filings.Count,
            run.Recorded,
            run.WithoutValues,
            run.AlreadyHeld,
            run.LinesRecorded,
            run.LinesRestated,
            run.Late,
            run.Rejections,
            run.Withheld,
            run.Dropped);

        ApplicationLog.FundamentalsImported(
            logger, source.Code.Value, report.FilingsRead, report.ReportsRecorded, report.ReportsWithoutValues,
            report.ReportsAlreadyHeld, report.LinesRecorded, report.LinesRestated, report.Late.Count);

        foreach (var rejection in report.Rejections)
        {
            ApplicationLog.FilingRejected(logger, rejection.Reference, rejection.Ticker, rejection.Reason, rejection.Detail);
        }

        return report;
    }

    private static ReadFiling Read(ProviderFiling filing)
    {
        var classification = FilingClassifier.Classify(filing, out var refused);

        if (classification is null)
        {
            return new ReadFiling(null, null, refused);
        }

        var normalised = StatementNormaliser.Normalise(filing, classification, out refused);
        return new ReadFiling(classification, normalised, refused);
    }

    private async Task ImportAsync(
        Run run,
        ProviderFiling filing,
        ReadFiling read,
        FilingRejection? withheld,
        Issuer issuer,
        CancellationToken cancellationToken)
    {
        if (read is not { Classification: { } classification, Normalised: { } normalised })
        {
            run.Rejections.Add(read.Refused!);
            return;
        }

        // Reported only where the values are recorded: a withheld form's
        // lines are dropped anyway.
        foreach (var line in withheld is null ? normalised.Dropped ?? [] : [])
        {
            run.Dropped.Add(new FilingRejection(filing.Reference, filing.Ticker, FilingRejectionReason.ImplausibleLine, line));
        }

        var now = clock.UtcNow;
        var existing = await fundamentals.FindReportAsync(run.Source.Code, filing.Reference, cancellationToken)
            .ConfigureAwait(false);

        if (withheld is not null)
        {
            // Append-only: values recorded before the rule existed stay, and
            // are reported for the operator to purge, never quietly kept.
            if (existing is { ValuesFiled: true })
            {
                run.Rejections.Add(withheld with
                {
                    Detail = withheld.Detail + " It is already held with values; purge it and import again.",
                });
                return;
            }

            run.Withheld.Add(withheld);
            normalised = normalised with { Values = [] };
        }

        RawFundamentalsPayload raw;

        try
        {
            // Validated first, so an oversized payload refuses the filing
            // before any of it is staged.
            raw = RawFundamentalsPayload.Retain(
                run.Source.Code, filing.Reference, issuer.InstrumentId, filing.RawPayload, filing.ContentType, filing.FetchedAtUtc);

            if (existing is null)
            {
                Record(run, filing, issuer, classification, normalised, now);
            }
            else
            {
                if (!await RereadAsync(run, filing, existing, normalised, now, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (DomainException error)
        {
            // Raised before anything was staged, from the domain's own checks:
            // a value with more places than the column keeps, a title too long.
            run.Rejections.Add(new FilingRejection(filing.Reference, filing.Ticker, FilingRejectionReason.Unreadable, error.Message));
            return;
        }

        if (!await fundamentals.HoldsPayloadAsync(run.Source.Code, filing.Reference, raw.Checksum, cancellationToken).ConfigureAwait(false))
        {
            fundamentals.Add(raw);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (classification.Late is { } late && existing is null)
        {
            run.Late.Add(late);
        }
    }

    private void Record(
        Run run,
        ProviderFiling filing,
        Issuer issuer,
        FilingClassification classification,
        NormalisedFiling normalised,
        DateTimeOffset now)
    {
        var valuesFiled = normalised.Values.Count > 0;

        var report = FinancialReport.Record(
            issuer.InstrumentId,
            run.Source.Code,
            filing.Reference,
            classification.Kind,
            classification.Scope,
            normalised.Template,
            classification.Period,
            filing.ReportName,
            // The later of the sent and indexed dates: the values read are the
            // filing as it stands at the later one. A time belongs to the
            // sent date, so it is kept only when that is the date used.
            filing.DisclosedOn,
            filing.DisclosedOn == filing.SubmittedOn ? filing.SubmittedAtUtc : null,
            issuer.TimeZone,
            classification.IsCorrection,
            valuesFiled,
            now);

        // Build every line before staging any, so a value the domain refuses
        // leaves nothing of the report behind.
        var lines = normalised.Values
            .Select(value => StatementLine.Observe(
                report, value.Statement, value.LineCode, value.Period, value.Span, value.Value, run.Source.Basis, run.Source.Code, now))
            .ToList();

        fundamentals.Add(report);

        foreach (var line in lines)
        {
            fundamentals.Add(line);
        }

        run.LinesRecorded += lines.Count;

        if (valuesFiled)
        {
            run.Recorded++;
        }
        else
        {
            run.WithoutValues++;
        }
    }

    private async Task<bool> RereadAsync(
        Run run,
        ProviderFiling filing,
        FinancialReport report,
        NormalisedFiling normalised,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        run.AlreadyHeld++;

        if (!report.ValuesFiled && normalised.Values.Count > 0)
        {
            run.Rejections.Add(new FilingRejection(
                filing.Reference,
                filing.Ticker,
                FilingRejectionReason.ValuesAppeared,
                "The report was recorded without values and now has some. Its date may not be when they appeared."));
            return false;
        }

        var current = (await fundamentals.ListCurrentLinesForUpdateAsync(report.Id, cancellationToken).ConfigureAwait(false))
            .ToDictionary(line => (line.Statement, line.LineCode, line.Period, line.Span));

        var added = new List<StatementLine>();
        var changed = new List<(StatementLine Line, decimal Value)>();

        foreach (var value in normalised.Values)
        {
            var key = (value.Statement, value.LineCode, value.Period, value.Span);

            if (!current.TryGetValue(key, out var line))
            {
                added.Add(StatementLine.Observe(
                    report, value.Statement, value.LineCode, value.Period, value.Span, value.Value, run.Source.Basis, run.Source.Code, now));
            }
            else if (!line.States(value.Value, run.Source.Basis))
            {
                changed.Add((line, value.Value));
            }
        }

        // Restating closes the old reading on a tracked entity, so it comes
        // last, once nothing else can refuse the filing: a refusal after it
        // would leave a closed window for the next filing's save to write.
        if (changed.Any(change => now <= change.Line.ObservedFromUtc))
        {
            throw new InvalidOperationException(
                $"The clock reads {now:O}, not after a reading already held for {filing.Reference}; refusing to restate into the past.");
        }

        var restated = changed
            .Select(change => change.Line.Restate(change.Value, run.Source.Basis, run.Source.Code, now))
            .ToList();

        foreach (var line in added.Concat(restated))
        {
            fundamentals.Add(line);
        }

        run.LinesRecorded += added.Count;
        run.LinesRestated += restated.Count;
        return true;
    }

    private async Task<Issuer?> ResolveAsync(string ticker, CancellationToken cancellationToken)
    {
        var resolution = await instruments.ResolveAsync(ticker, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (resolution.Outcome != InstrumentResolutionOutcome.Resolved || resolution.Instrument is not { } instrument)
        {
            return null;
        }

        var venue = await exchanges.FindByCodeAsync(instrument.ExchangeCode, cancellationToken).ConfigureAwait(false);

        return venue is null
            ? null
            : new Issuer(instrument.InstrumentId, TimeZoneInfo.FindSystemTimeZoneById(venue.TimeZoneId));
    }

    private sealed record Issuer(InstrumentId InstrumentId, TimeZoneInfo TimeZone);

    /// <summary>A filing classified and normalised, or the reason it could not be.</summary>
    /// <param name="Classification">What it is, or null when refused.</param>
    /// <param name="Normalised">Its values, or null when refused.</param>
    /// <param name="Refused">Why it was refused.</param>
    internal sealed record ReadFiling(
        FilingClassification? Classification,
        NormalisedFiling? Normalised,
        FilingRejection? Refused);

    private sealed class Run(IFundamentalsProvider source)
    {
        public IFundamentalsProvider Source { get; } = source;

        public List<FilingRejection> Rejections { get; } = [];

        public List<FilingRejection> Withheld { get; } = [];

        public List<FilingRejection> Dropped { get; } = [];

        public List<LateFiling> Late { get; } = [];

        public int Recorded { get; set; }

        public int WithoutValues { get; set; }

        public int AlreadyHeld { get; set; }

        public int LinesRecorded { get; set; }

        public int LinesRestated { get; set; }
    }
}
