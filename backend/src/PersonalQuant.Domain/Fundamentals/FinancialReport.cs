using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Fundamentals;

/// <summary>Identifies a financial report.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct FinancialReportId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static FinancialReportId New() => new(Guid.CreateVersion7());

    /// <summary>Gets a value indicating whether this is the default, unassigned identifier.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One periodic financial report as it was disclosed: the record that says
/// <em>when</em> a set of statement values became public.
/// </summary>
/// <remarks>
/// <para>
/// ADR-029: a fundamental fact enters PQT only when a disclosure record dates
/// it. This is that record. Its values are <see cref="StatementLine"/>s, and a
/// value is knowable exactly when its report is, from
/// <see cref="KnownFromUtc"/>.
/// </para>
/// <para>
/// A correction is a new report with its own date, never an edit of this one.
/// A later report that restates an earlier period, in its comparative columns
/// or as a correction, adds lines to <em>its own</em> report. So what was
/// known at an instant is the latest report known by then, and nothing here is
/// ever overwritten.
/// </para>
/// <para>
/// A report whose structured form was left empty is still a disclosure. It is
/// recorded with <see cref="ValuesFiled"/> false and carries no lines, so the
/// period shows as disclosed without values and never as a row of zeros.
/// </para>
/// </remarks>
public sealed class FinancialReport
{
    /// <summary>The longest source reference accepted.</summary>
    public const int MaxSourceReferenceLength = 256;

    /// <summary>The longest title accepted.</summary>
    public const int MaxTitleLength = 512;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private FinancialReport()
    {
        Source = null!;
        SourceReference = null!;
        Title = null!;
    }

    private FinancialReport(
        InstrumentId instrumentId,
        SourceCode source,
        string sourceReference,
        FinancialReportKind kind,
        FinancialStatementScope scope,
        FinancialTemplate template,
        FiscalQuarter period,
        string title,
        DateOnly disclosedOn,
        DateTimeOffset? disclosedAtUtc,
        DateTimeOffset knownFromUtc,
        bool isCorrection,
        bool valuesFiled,
        DateTimeOffset recordedAtUtc)
    {
        Id = FinancialReportId.New();
        InstrumentId = instrumentId;
        Source = source;
        SourceReference = sourceReference;
        Kind = kind;
        Scope = scope;
        Template = template;
        Period = period;
        Title = title;
        DisclosedOn = disclosedOn;
        DisclosedAtUtc = disclosedAtUtc;
        KnownFromUtc = knownFromUtc;
        IsCorrection = isCorrection;
        ValuesFiled = valuesFiled;
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Gets the report's identifier.</summary>
    public FinancialReportId Id { get; private set; }

    /// <summary>Gets the issuer's instrument.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the disclosure system the record was read from, such as IDS.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the source's own key for the filing, unique within the source.</summary>
    public string SourceReference { get; private set; }

    /// <summary>Gets which periodic report this is.</summary>
    public FinancialReportKind Kind { get; private set; }

    /// <summary>Gets whose accounts it states.</summary>
    public FinancialStatementScope Scope { get; private set; }

    /// <summary>Gets the statement form its line codes belong to.</summary>
    public FinancialTemplate Template { get; private set; }

    /// <summary>Gets the fiscal quarter the report closes on.</summary>
    public FiscalQuarter Period { get; private set; }

    /// <summary>Gets the title as the source listed it.</summary>
    public string Title { get; private set; }

    /// <summary>Gets the venue-local date the source dates the disclosure.</summary>
    public DateOnly DisclosedOn { get; private set; }

    /// <summary>Gets the instant of disclosure, when the source gives a time of day.</summary>
    public DateTimeOffset? DisclosedAtUtc { get; private set; }

    /// <summary>Gets the first instant the report's values may be used. Inclusive.</summary>
    /// <remarks>
    /// ADR-029, decision 2. With a time of day it is that instant. With a date
    /// alone it is the start of the next local day: the record cannot say
    /// whether the filing came before or after the close, so it is assumed to
    /// have come after.
    /// </remarks>
    public DateTimeOffset KnownFromUtc { get; private set; }

    /// <summary>Gets a value indicating whether the source marks the filing as a correction.</summary>
    public bool IsCorrection { get; private set; }

    /// <summary>Gets a value indicating whether structured values were filed with it.</summary>
    public bool ValuesFiled { get; private set; }

    /// <summary>Gets when this system recorded the report.</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Records a disclosed report.</summary>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="source">The disclosure system.</param>
    /// <param name="sourceReference">The source's key for the filing.</param>
    /// <param name="kind">Which periodic report it is.</param>
    /// <param name="scope">Whose accounts it states.</param>
    /// <param name="template">The statement form.</param>
    /// <param name="period">The fiscal quarter it closes on.</param>
    /// <param name="title">The title as listed.</param>
    /// <param name="disclosedOn">The venue-local disclosure date.</param>
    /// <param name="disclosedAtUtc">The disclosure instant, when the source gives one.</param>
    /// <param name="venueTimeZone">The venue's time zone, which turns a local date into an instant.</param>
    /// <param name="isCorrection">Whether the source marks it as a correction.</param>
    /// <param name="valuesFiled">Whether structured values were filed with it.</param>
    /// <param name="recordedAtUtc">When it is recorded.</param>
    /// <returns>The report.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static FinancialReport Record(
        InstrumentId instrumentId,
        SourceCode source,
        string sourceReference,
        FinancialReportKind kind,
        FinancialStatementScope scope,
        FinancialTemplate template,
        FiscalQuarter period,
        string title,
        DateOnly disclosedOn,
        DateTimeOffset? disclosedAtUtc,
        TimeZoneInfo venueTimeZone,
        bool isCorrection,
        bool valuesFiled,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(venueTimeZone);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A financial report must name an instrument.");
        }

        RequireDefined(kind, FinancialReportKind.Unspecified, "report kind");
        RequireDefined(scope, FinancialStatementScope.Unspecified, "statement scope");
        RequireDefined(template, FinancialTemplate.Unspecified, "template");

        var reference = RequireText(sourceReference, MaxSourceReferenceLength, "source reference");
        var trimmedTitle = RequireText(title, MaxTitleLength, "title");

        // The kind fixes where the report closes. A half-year report that
        // claimed the third quarter would put nine months under a six-month
        // label.
        var expectedQuarter = kind switch
        {
            FinancialReportKind.SemiAnnualReviewed => 2,
            FinancialReportKind.AnnualAudited => 4,
            _ => period.Quarter,
        };

        if (period.Quarter != expectedQuarter)
        {
            throw new DomainValidationException(
                $"A {kind} report closes on quarter {expectedQuarter}, but {period} was given.");
        }

        RequireUtc(recordedAtUtc);

        var knownFromUtc = KnownFrom(disclosedOn, disclosedAtUtc, venueTimeZone);

        if (disclosedOn > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(recordedAtUtc, venueTimeZone).DateTime))
        {
            throw new DomainValidationException(
                $"A report cannot be recorded before it is disclosed; {disclosedOn:yyyy-MM-dd} is after the recording date.");
        }

        return new FinancialReport(
            instrumentId,
            source,
            reference,
            kind,
            scope,
            template,
            period,
            trimmedTitle,
            disclosedOn,
            disclosedAtUtc,
            knownFromUtc,
            isCorrection,
            valuesFiled,
            recordedAtUtc);
    }

    private static DateTimeOffset KnownFrom(DateOnly disclosedOn, DateTimeOffset? disclosedAtUtc, TimeZoneInfo zone)
    {
        if (disclosedAtUtc is { } at)
        {
            RequireUtc(at);

            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

            if (localDate != disclosedOn)
            {
                throw new DomainValidationException(
                    $"The disclosure instant falls on {localDate:yyyy-MM-dd} locally, not on {disclosedOn:yyyy-MM-dd}.");
            }

            return at;
        }

        var nextMidnight = disclosedOn.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextMidnight, zone), TimeSpan.Zero);
    }

    private static void RequireDefined<TEnum>(TEnum value, TEnum unspecified, string name)
        where TEnum : struct, Enum
    {
        if (value.Equals(unspecified) || !Enum.IsDefined(value))
        {
            throw new DomainValidationException($"A financial report needs a {name}, but '{value}' was given.");
        }
    }

    private static string RequireText(string? value, int maxLength, string name)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException($"A financial report needs a {name}.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(
                $"A financial report's {name} must be at most {maxLength} characters, but was {trimmed.Length}.");
        }

        return trimmed;
    }

    private static void RequireUtc(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException($"Timestamps must be UTC, but the offset was {instant.Offset}.");
        }
    }
}
