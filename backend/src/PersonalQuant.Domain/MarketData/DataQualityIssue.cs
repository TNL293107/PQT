using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>The identifier of a <see cref="DataQualityIssue"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct DataQualityIssueId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static DataQualityIssueId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a quality check found.
/// </summary>
/// <remarks>
/// <para>
/// Only checks that need context beyond a single row. What one bar can say
/// about itself — a high below its close, a negative volume, a price of zero —
/// is a domain invariant enforced at construction, so such a row never reaches
/// storage and never becomes an issue. Duplicates are the same: the storage key
/// makes them impossible.
/// </para>
/// <para>
/// Values are explicit because they are persisted and appear in dashboards that
/// will outlive this enum's declaration order.
/// </para>
/// </remarks>
public enum DataQualityIssueKind
{
    /// <summary>
    /// A session-to-session move larger than the venue's daily price limit
    /// permits.
    /// </summary>
    /// <remarks>
    /// The sharpest signal available in this market. HOSE allows ±7%, HNX ±10%
    /// and UPCOM ±15%, and the exchange rejects orders outside the band — so a
    /// larger move did not happen the way the numbers claim. It is a corporate
    /// action, a bad print, a halt, or a symbol change, and which of those it is
    /// cannot be decided from the prices alone.
    /// </remarks>
    PriceLimitBreach = 1,

    /// <summary>
    /// A day the calendar says the venue traded, with no bar stored for it.
    /// </summary>
    MissingSession = 2,

    /// <summary>
    /// A bar on a day the calendar says the venue was closed.
    /// </summary>
    /// <remarks>
    /// Usually the calendar being wrong rather than the data: a holiday that
    /// was cancelled, or a substitute day nobody recorded. Raised anyway,
    /// because a calendar that is quietly wrong makes every completeness figure
    /// computed against it wrong too.
    /// </remarks>
    UnexpectedSession = 3,
}

/// <summary>Where an issue stands.</summary>
public enum DataQualityIssueStatus
{
    /// <summary>Detected and not yet accounted for.</summary>
    Open = 1,

    /// <summary>
    /// Accounted for by something known — a corporate action, a halt, a symbol
    /// change.
    /// </summary>
    Explained = 2,

    /// <summary>Investigated and found not to be a problem.</summary>
    Dismissed = 3,
}

/// <summary>
/// Something the quality rules found that a single bar could not have revealed.
/// </summary>
/// <remarks>
/// <para>
/// The bar is stored regardless. Refusing data because it looks wrong would
/// lose the only record of what the source actually said, and a series with a
/// hole where a corporate action happened is worse than one with a flag on it.
/// What is refused is <em>silence</em>: the discontinuity is written down, and
/// a consumer that cannot tolerate an unexplained one can see it and stop.
/// </para>
/// <para>
/// Nothing is corrected automatically. Phase 4 explains price-limit breaches by
/// matching them to corporate actions; until then they stay open, which is an
/// honest description of what the system knows.
/// </para>
/// <para>
/// One issue per instrument, resolution, session and kind — enforced by the
/// schema. A nightly run that re-reads the same range must not raise the same
/// finding again, and a dismissal must not be undone by the next ingestion.
/// </para>
/// </remarks>
public sealed class DataQualityIssue
{
    /// <summary>Longest permitted detail or resolution text.</summary>
    public const int MaxTextLength = 500;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private DataQualityIssue() => Detail = null!;

    private DataQualityIssue(
        DataQualityIssueId id,
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset sessionAtUtc,
        DataQualityIssueKind kind,
        string detail,
        int validationVersion,
        DateTimeOffset detectedAtUtc)
    {
        Id = id;
        InstrumentId = instrumentId;
        Interval = interval;
        SessionAtUtc = sessionAtUtc;
        Kind = kind;
        Detail = detail;
        ValidationVersion = validationVersion;
        DetectedAtUtc = detectedAtUtc;
        Status = DataQualityIssueStatus.Open;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public DataQualityIssueId Id { get; private set; }

    /// <summary>Gets the instrument the finding concerns.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the finding concerns.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>
    /// Gets the opening instant of the session the finding is about.
    /// </summary>
    /// <remarks>
    /// For a missing session there is no bar at this instant — that is the
    /// finding. The timestamp is still the period's opening edge, so an issue
    /// and the bar it concerns line up on the same key.
    /// </remarks>
    public DateTimeOffset SessionAtUtc { get; private set; }

    /// <summary>Gets what was found.</summary>
    public DataQualityIssueKind Kind { get; private set; }

    /// <summary>Gets the specifics, including the numbers that triggered it.</summary>
    public string Detail { get; private set; }

    /// <summary>Gets the rule version that raised the finding.</summary>
    public int ValidationVersion { get; private set; }

    /// <summary>Gets the instant it was found, in UTC.</summary>
    public DateTimeOffset DetectedAtUtc { get; private set; }

    /// <summary>Gets where the finding stands.</summary>
    public DataQualityIssueStatus Status { get; private set; }

    /// <summary>Gets the instant it stopped being open, if it has.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>Gets what accounted for it, once something did.</summary>
    public string? Resolution { get; private set; }

    /// <summary>Gets a value indicating whether the finding is still unexplained.</summary>
    public bool IsOpen => Status == DataQualityIssueStatus.Open;

    /// <summary>
    /// Raises a finding.
    /// </summary>
    /// <param name="instrumentId">The instrument concerned.</param>
    /// <param name="interval">The resolution concerned.</param>
    /// <param name="sessionAtUtc">The session's opening instant.</param>
    /// <param name="kind">What was found.</param>
    /// <param name="detail">The specifics.</param>
    /// <param name="validationVersion">The rule version that found it.</param>
    /// <param name="detectedAtUtc">The instant it was found.</param>
    /// <returns>The new finding, open.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static DataQualityIssue Raise(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset sessionAtUtc,
        DataQualityIssueKind kind,
        string detail,
        int validationVersion,
        DateTimeOffset detectedAtUtc)
    {
        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A quality issue must concern an instrument.");
        }

        if (!interval.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{interval}' is not a bar resolution this system records.");
        }

        if (!interval.IsAligned(sessionAtUtc))
        {
            throw new DomainValidationException(
                $"A {interval} issue cannot sit at {sessionAtUtc:O}; it is not on a period boundary in UTC.");
        }

        if (validationVersion <= DataRules.Unvalidated)
        {
            // A finding raised by "no rules" cannot be re-evaluated when the
            // rules change, which is most of what the version is for.
            throw new DomainValidationException(
                "A quality issue must record the rule version that raised it.");
        }

        return new DataQualityIssue(
            DataQualityIssueId.New(),
            instrumentId,
            interval,
            sessionAtUtc,
            kind,
            RequireText(detail, "A quality issue must say what was found."),
            validationVersion,
            detectedAtUtc);
    }

    /// <summary>
    /// Records that something known accounts for the finding.
    /// </summary>
    /// <param name="resolution">What accounts for it.</param>
    /// <param name="resolvedAtUtc">The instant it was accounted for.</param>
    /// <exception cref="DomainStateException">The finding is not open.</exception>
    public void Explain(string resolution, DateTimeOffset resolvedAtUtc) =>
        Close(DataQualityIssueStatus.Explained, resolution, resolvedAtUtc);

    /// <summary>
    /// Records that the finding was investigated and is not a problem.
    /// </summary>
    /// <param name="reason">Why it is not a problem.</param>
    /// <param name="resolvedAtUtc">The instant it was dismissed.</param>
    /// <exception cref="DomainStateException">The finding is not open.</exception>
    public void Dismiss(string reason, DateTimeOffset resolvedAtUtc) =>
        Close(DataQualityIssueStatus.Dismissed, reason, resolvedAtUtc);

    private void Close(DataQualityIssueStatus status, string text, DateTimeOffset resolvedAtUtc)
    {
        if (!IsOpen)
        {
            // Reopening by overwriting a resolution would erase the audit
            // trail the issue exists to leave.
            throw new DomainStateException(
                $"Quality issue {Id} is already {Status} and cannot be resolved again.");
        }

        Status = status;
        Resolution = RequireText(text, "A resolution must say what accounts for the issue.");
        ResolvedAtUtc = resolvedAtUtc;
    }

    private static string RequireText(string? text, string message)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainValidationException(message);
        }

        var trimmed = text.Trim();

        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}
