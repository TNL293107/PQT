using System.Globalization;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Fundamentals;

/// <summary>Identifies a statement line.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct StatementLineId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static StatementLineId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One value a report states for one line of one statement, for one period.
/// </summary>
/// <remarks>
/// <para>
/// There are two kinds of time here, and they are not interchangeable.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <em>When the market could know it</em> belongs to the report:
/// <see cref="FinancialReport.KnownFromUtc"/>. A later report that restates
/// this period, in its comparative columns or as a correction, states it again
/// as a line of <em>that</em> report. The earlier line is untouched.
/// </description>
/// </item>
/// <item>
/// <description>
/// <em>When this system held it</em> belongs to the line:
/// [<see cref="ObservedFromUtc"/>, <see cref="ObservedToUtc"/>). If the same
/// report is read again and a value has changed, which happens when a vendor
/// rewrites history, the line is superseded by a new revision, as
/// <see cref="BarRevision"/> does for prices.
/// </description>
/// </item>
/// </list>
/// <para>
/// Append-only. A line is never updated except to close its observation
/// window, and never deleted.
/// </para>
/// </remarks>
public sealed class StatementLine
{
    /// <summary>The longest line code accepted, after normalisation.</summary>
    public const int MaxLineCodeLength = 8;

    /// <summary>Decimal places kept on a value. The stored column has no more.</summary>
    public const int ValueScale = 4;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private StatementLine()
    {
        LineCode = null!;
        Source = null!;
    }

    private StatementLine(
        FinancialReportId reportId,
        InstrumentId instrumentId,
        FinancialStatement statement,
        string lineCode,
        FiscalQuarter period,
        PeriodSpan span,
        decimal value,
        ValueBasis basis,
        SourceCode source,
        int revision,
        DateTimeOffset observedFromUtc)
    {
        Id = StatementLineId.New();
        ReportId = reportId;
        InstrumentId = instrumentId;
        Statement = statement;
        LineCode = lineCode;
        Period = period;
        Span = span;
        Value = value;
        Basis = basis;
        Source = source;
        Revision = revision;
        ObservedFromUtc = observedFromUtc;
    }

    /// <summary>Gets the line's identifier.</summary>
    public StatementLineId Id { get; private set; }

    /// <summary>Gets the report that states the value.</summary>
    public FinancialReportId ReportId { get; private set; }

    /// <summary>Gets the issuer's instrument, copied from the report so reads need not join.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the statement the line is on.</summary>
    public FinancialStatement Statement { get; private set; }

    /// <summary>Gets the form's line code (<em>mã số</em>), normalised.</summary>
    /// <remarks>Meaningful only with the report's <see cref="FinancialReport.Template"/>.</remarks>
    public string LineCode { get; private set; }

    /// <summary>Gets the quarter the value is anchored to.</summary>
    public FiscalQuarter Period { get; private set; }

    /// <summary>Gets the stretch of time the value covers.</summary>
    public PeriodSpan Span { get; private set; }

    /// <summary>Gets the value, in đồng for money and as stated for ratios such as EPS.</summary>
    public decimal Value { get; private set; }

    /// <summary>Gets where the value was read.</summary>
    public ValueBasis Basis { get; private set; }

    /// <summary>Gets the source the value was read from, which may differ from the report's.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets which reading of this line of this report it is, counting from zero.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets when this system began holding the value. Inclusive.</summary>
    public DateTimeOffset ObservedFromUtc { get; private set; }

    /// <summary>Gets when the value was superseded, or null while it is current. Exclusive.</summary>
    public DateTimeOffset? ObservedToUtc { get; private set; }

    /// <summary>Gets a value indicating whether this is the reading currently held.</summary>
    public bool IsCurrent => ObservedToUtc is null;

    /// <summary>Records the first reading of a value a report states.</summary>
    /// <param name="report">The report.</param>
    /// <param name="statement">The statement the line is on.</param>
    /// <param name="lineCode">The form's line code, as the source wrote it.</param>
    /// <param name="period">The quarter the value is anchored to.</param>
    /// <param name="span">The stretch of time it covers.</param>
    /// <param name="value">The value.</param>
    /// <param name="basis">Where it was read.</param>
    /// <param name="source">The source it was read from.</param>
    /// <param name="observedFromUtc">When this system began holding it.</param>
    /// <returns>The line.</returns>
    /// <exception cref="DomainStateException">The report was recorded as having no values.</exception>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static StatementLine Observe(
        FinancialReport report,
        FinancialStatement statement,
        string lineCode,
        FiscalQuarter period,
        PeriodSpan span,
        decimal value,
        ValueBasis basis,
        SourceCode source,
        DateTimeOffset observedFromUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(source);

        if (!report.ValuesFiled)
        {
            throw new DomainStateException(
                $"Report {report.SourceReference} was recorded without values, so it states no lines.");
        }

        if (statement == FinancialStatement.Unspecified || !Enum.IsDefined(statement))
        {
            throw new DomainValidationException($"A statement line needs a statement, but '{statement}' was given.");
        }

        if (basis == ValueBasis.Unspecified || !Enum.IsDefined(basis))
        {
            throw new DomainValidationException($"A statement line needs a basis, but '{basis}' was given.");
        }

        // A balance is a stock at an instant and everything else a flow over a
        // span. A balance with a span, or a flow without one, is a column read
        // into the wrong place.
        var spanFits = statement == FinancialStatement.BalanceSheet
            ? span == PeriodSpan.Instant
            : span is PeriodSpan.Quarter or PeriodSpan.YearToDate;

        if (!spanFits)
        {
            throw new DomainValidationException($"A {statement} line cannot cover a {span} span.");
        }

        if (period > report.Period)
        {
            throw new DomainValidationException(
                $"A report closing on {report.Period} cannot state a value for the later period {period}.");
        }

        RequireScale(value);
        RequireUtc(observedFromUtc);

        return new StatementLine(
            report.Id,
            report.InstrumentId,
            statement,
            NormaliseLineCode(lineCode),
            period,
            span,
            value,
            basis,
            source,
            revision: 0,
            observedFromUtc);
    }

    /// <summary>
    /// Normalises a form's line code so two sources writing one line agree.
    /// </summary>
    /// <remarks>
    /// IDS writes <c>01</c>, <c>411a</c> and, on one insurer's form,
    /// <c>.37</c>. The digits keep at least two places, since the income
    /// statement's codes are two-digit, and a trailing letter is lower-cased.
    /// </remarks>
    /// <param name="lineCode">The code as written.</param>
    /// <returns>The normalised code.</returns>
    /// <exception cref="DomainValidationException">The code is not a line code.</exception>
    public static string NormaliseLineCode(string? lineCode)
    {
        var text = lineCode?.Trim().TrimStart('.').ToLowerInvariant() ?? string.Empty;
        var digits = text.TrimEnd('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h');
        var suffix = text[digits.Length..];

        if (digits.Length is 0 or > 6 || !digits.All(char.IsAsciiDigit) || suffix.Length > 1)
        {
            throw new DomainValidationException($"'{lineCode}' is not a statement line code.");
        }

        var number = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return number.ToString("00", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>Gets a value indicating whether this reading already states a value.</summary>
    /// <param name="value">The value read.</param>
    /// <param name="basis">Where it was read.</param>
    /// <returns><see langword="true"/> when nothing has changed.</returns>
    public bool States(decimal value, ValueBasis basis) => Value == value && Basis == basis;

    /// <summary>
    /// Supersedes this reading with a changed one, closing this window at the
    /// instant the next opens.
    /// </summary>
    /// <param name="value">The value now read.</param>
    /// <param name="basis">Where it was read.</param>
    /// <param name="source">The source it was read from.</param>
    /// <param name="observedAtUtc">When this system began holding it.</param>
    /// <returns>The next revision.</returns>
    /// <exception cref="DomainStateException">This reading is not current, or nothing changed.</exception>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public StatementLine Restate(decimal value, ValueBasis basis, SourceCode source, DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!IsCurrent)
        {
            throw new DomainStateException("Only the current reading of a line can be restated.");
        }

        if (States(value, basis))
        {
            throw new DomainStateException("A restatement must change the value or its basis.");
        }

        if (basis == ValueBasis.Unspecified || !Enum.IsDefined(basis))
        {
            throw new DomainValidationException($"A statement line needs a basis, but '{basis}' was given.");
        }

        RequireScale(value);
        RequireUtc(observedAtUtc);

        if (observedAtUtc <= ObservedFromUtc)
        {
            throw new DomainValidationException(
                "A restatement must be observed after the reading it supersedes.");
        }

        ObservedToUtc = observedAtUtc;

        return new StatementLine(
            ReportId,
            InstrumentId,
            Statement,
            LineCode,
            Period,
            Span,
            value,
            basis,
            source,
            Revision + 1,
            observedAtUtc);
    }

    private static void RequireScale(decimal value)
    {
        if (decimal.Round(value, ValueScale) != value)
        {
            throw new DomainValidationException(
                $"A statement value keeps at most {ValueScale} decimal places, but {value} has more.");
        }
    }

    private static void RequireUtc(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException($"Timestamps must be UTC, but the offset was {instant.Offset}.");
        }
    }
}
