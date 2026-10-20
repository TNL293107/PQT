using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// Reads an issuer's statement values as they could have been known at an
/// instant (ADR-029; sprint 6 item 6.4).
/// </summary>
public interface IFundamentalsReader
{
    /// <summary>Reads every value of one scope known at an instant.</summary>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="scope">Separate or consolidated; the two are never mixed.</param>
    /// <param name="knownAsOfUtc">
    /// The instant the market's knowledge is read at. A report becomes known at
    /// its <see cref="FinancialReport.KnownFromUtc"/>, inclusive.
    /// </param>
    /// <param name="observedAsOfUtc">
    /// The instant this system's knowledge is read at, for reproducing an
    /// earlier read: reports recorded later are absent and each line is read
    /// as it stood then. Null reads what PQT holds now.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The values, one per line and period, and what was withheld.</returns>
    Task<FundamentalsAsOf> ReadAsync(
        InstrumentId instrumentId,
        FinancialStatementScope scope,
        DateTimeOffset knownAsOfUtc,
        DateTimeOffset? observedAsOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an issuer's reports and lines once, to be read at many instants.
    /// </summary>
    /// <remarks>
    /// A read of each session of a dataset would otherwise load the same
    /// rows again for every session. <see cref="FundamentalsHistory.Read"/>
    /// at an instant equals <see cref="ReadAsync"/> at it.
    /// </remarks>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="observedAsOfUtc">
    /// The instant this system's knowledge is read at, as for
    /// <see cref="ReadAsync"/>; null reads what PQT holds now.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The loaded history.</returns>
    Task<FundamentalsHistory> LoadAsync(
        InstrumentId instrumentId,
        DateTimeOffset? observedAsOfUtc = null,
        CancellationToken cancellationToken = default);
}

/// <summary>An issuer's statement values as known at an instant.</summary>
/// <param name="InstrumentId">The issuer's instrument.</param>
/// <param name="Scope">The scope read.</param>
/// <param name="KnownAsOfUtc">The market-knowledge instant.</param>
/// <param name="ObservedAsOfUtc">The system-knowledge instant, or null for now.</param>
/// <param name="Values">One value per template, statement, line, period and span.</param>
/// <param name="DisclosedWithoutValues">Reports known by then whose form was empty.</param>
/// <param name="ReportsKnown">Reports of this scope known by then.</param>
/// <param name="ReportsWithheld">Reports of this scope held now but not yet known then.</param>
public sealed record FundamentalsAsOf(
    InstrumentId InstrumentId,
    FinancialStatementScope Scope,
    DateTimeOffset KnownAsOfUtc,
    DateTimeOffset? ObservedAsOfUtc,
    IReadOnlyList<FundamentalValue> Values,
    IReadOnlyList<FinancialReport> DisclosedWithoutValues,
    int ReportsKnown,
    int ReportsWithheld)
{
    /// <summary>Gets how many values served were read from the issuer's own form.</summary>
    public int AsFiled => Values.Count(value => value.Basis == ValueBasis.AsFiled);

    /// <summary>Gets how many values served were read from an aggregator.</summary>
    public int Vendor => Values.Count(value => value.Basis == ValueBasis.Vendor);
}

/// <summary>One statement value, as known at an instant, and where it came from.</summary>
/// <param name="Template">The form its line code belongs to.</param>
/// <param name="Statement">The statement.</param>
/// <param name="LineCode">The form's line code.</param>
/// <param name="Period">The quarter it is anchored to.</param>
/// <param name="Span">The stretch of time it covers.</param>
/// <param name="Value">The value.</param>
/// <param name="Basis">Where it was read.</param>
/// <param name="ReportId">The report that stated it.</param>
/// <param name="ReportKind">That report's kind.</param>
/// <param name="KnownFromUtc">When that report became known.</param>
/// <param name="Restated">Whether an earlier known report stated a different value.</param>
public sealed record FundamentalValue(
    FinancialTemplate Template,
    FinancialStatement Statement,
    string LineCode,
    FiscalQuarter Period,
    PeriodSpan Span,
    decimal Value,
    ValueBasis Basis,
    FinancialReportId ReportId,
    FinancialReportKind ReportKind,
    DateTimeOffset KnownFromUtc,
    bool Restated);

/// <summary>
/// Serves statement values point in time.
/// </summary>
/// <remarks>
/// <para>
/// Two cuts, as for prices. The market cut admits a report once its
/// <see cref="FinancialReport.KnownFromUtc"/> has passed, so a report filed
/// after the instant is absent, and so is every value it would have restated.
/// The observation cut, when given, admits only what this system had recorded
/// by then, and reads each line as it stood, so a read can be reproduced
/// after a vendor rewrote a value.
/// </para>
/// <para>
/// A period is stated by several reports: its own, the next year's
/// comparatives, the audited year, a correction. The value served is the one
/// in the latest report known by the instant. On the same instant, a
/// correction outranks what it corrects, and otherwise the later recording
/// wins. The line code's template is part of the key, because code 270 on
/// one form is not code 270 on another.
/// </para>
/// </remarks>
/// <param name="fundamentals">Reports and lines.</param>
public sealed class FundamentalsReader(IFundamentalsRepository fundamentals) : IFundamentalsReader
{
    /// <inheritdoc />
    public async Task<FundamentalsAsOf> ReadAsync(
        InstrumentId instrumentId,
        FinancialStatementScope scope,
        DateTimeOffset knownAsOfUtc,
        DateTimeOffset? observedAsOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(knownAsOfUtc, observedAsOfUtc);

        var history = await LoadAsync(instrumentId, observedAsOfUtc, cancellationToken).ConfigureAwait(false);
        return history.Read(scope, knownAsOfUtc);
    }

    /// <inheritdoc />
    public async Task<FundamentalsHistory> LoadAsync(
        InstrumentId instrumentId,
        DateTimeOffset? observedAsOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(DateTimeOffset.UnixEpoch, observedAsOfUtc);

        var held = (await fundamentals.ListReportsAsync([instrumentId], cancellationToken).ConfigureAwait(false))
            .Where(report => observedAsOfUtc is not { } observed || report.RecordedAtUtc <= observed)
            .ToList();

        var lines = (await fundamentals
                .ListLinesAsync([.. held.Where(report => report.ValuesFiled).Select(report => report.Id)], cancellationToken)
                .ConfigureAwait(false))
            .Where(line => ReadAt(line, observedAsOfUtc))
            .ToLookup(line => line.ReportId);

        return new FundamentalsHistory(instrumentId, observedAsOfUtc, held, lines);
    }

    private static void RequireUtc(DateTimeOffset knownAsOfUtc, DateTimeOffset? observedAsOfUtc)
    {
        if (knownAsOfUtc.Offset != TimeSpan.Zero || observedAsOfUtc is { Offset.Ticks: not 0 })
        {
            throw new ArgumentException("As-of instants must be UTC.", nameof(knownAsOfUtc));
        }
    }

    /// <summary>Whether a reading is the one held at the observation instant, or now.</summary>
    private static bool ReadAt(StatementLine line, DateTimeOffset? observedAsOfUtc) =>
        observedAsOfUtc is not { } observed
            ? line.IsCurrent
            : line.ObservedFromUtc <= observed && (line.ObservedToUtc is not { } to || observed < to);
}

/// <summary>
/// An issuer's reports and lines as this system held them at one instant,
/// to be read at any market instant (ADR-029; ADR-031).
/// </summary>
/// <remarks>
/// The market cut of <see cref="FundamentalsReader"/>, applied in memory:
/// a report is admitted once its <see cref="FinancialReport.KnownFromUtc"/>
/// has passed, and the latest one known states each value.
/// </remarks>
public sealed class FundamentalsHistory
{
    private readonly IReadOnlyList<FinancialReport> _held;

    private readonly ILookup<FinancialReportId, StatementLine> _lines;

    internal FundamentalsHistory(
        InstrumentId instrumentId,
        DateTimeOffset? observedAsOfUtc,
        IReadOnlyList<FinancialReport> held,
        ILookup<FinancialReportId, StatementLine> lines)
    {
        InstrumentId = instrumentId;
        ObservedAsOfUtc = observedAsOfUtc;
        _held = held;
        _lines = lines;
    }

    /// <summary>Gets the issuer's instrument.</summary>
    public InstrumentId InstrumentId { get; }

    /// <summary>Gets the system-knowledge instant, or null for now.</summary>
    public DateTimeOffset? ObservedAsOfUtc { get; }

    /// <summary>Gets whether any report is held at all, of either scope.</summary>
    public bool HasReports => _held.Count > 0;

    /// <summary>Gets the provider codes of the reports held, in ordinal order.</summary>
    public IReadOnlyList<string> Sources =>
        [.. _held.Select(report => report.Source.Value).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Reads every value of one scope known at an instant.</summary>
    /// <param name="scope">Separate or consolidated; the two are never mixed.</param>
    /// <param name="knownAsOfUtc">The market-knowledge instant, inclusive.</param>
    /// <returns>The values, one per line and period, and what was withheld.</returns>
    public FundamentalsAsOf Read(FinancialStatementScope scope, DateTimeOffset knownAsOfUtc)
    {
        if (knownAsOfUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("As-of instants must be UTC.", nameof(knownAsOfUtc));
        }

        var held = _held.Where(report => report.Scope == scope).ToList();

        // Oldest first, so a later report overwrites an earlier one per key.
        var known = held
            .Where(report => report.KnownFromUtc <= knownAsOfUtc)
            .OrderBy(report => report.KnownFromUtc)
            .ThenBy(report => report.IsCorrection)
            .ThenBy(report => report.RecordedAtUtc)
            .ThenBy(report => report.Id.Value)
            .ToList();

        var served = new Dictionary<(FinancialTemplate, FinancialStatement, string, FiscalQuarter, PeriodSpan), FundamentalValue>();

        foreach (var report in known.Where(report => report.ValuesFiled))
        {
            foreach (var line in _lines[report.Id])
            {
                var key = (report.Template, line.Statement, line.LineCode, line.Period, line.Span);
                var restated = served.TryGetValue(key, out var earlier) && (earlier.Restated || earlier.Value != line.Value);

                served[key] = new FundamentalValue(
                    report.Template,
                    line.Statement,
                    line.LineCode,
                    line.Period,
                    line.Span,
                    line.Value,
                    line.Basis,
                    report.Id,
                    report.Kind,
                    report.KnownFromUtc,
                    restated);
            }
        }

        return new FundamentalsAsOf(
            InstrumentId,
            scope,
            knownAsOfUtc,
            ObservedAsOfUtc,
            [.. served.Values
                .OrderBy(value => value.Statement)
                .ThenBy(value => value.Period)
                .ThenBy(value => value.Span)
                .ThenBy(value => value.LineCode, StringComparer.Ordinal)],
            [.. known.Where(report => !report.ValuesFiled)],
            known.Count,
            held.Count - known.Count);
    }
}
