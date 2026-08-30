using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Universes;

/// <summary>The identifier of a <see cref="UniverseCoverageFinding"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct UniverseCoverageFindingId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static UniverseCoverageFindingId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a coverage review found about a universe's membership history.
/// </summary>
/// <remarks>
/// <para>
/// Every value here describes an <em>absence</em>, which is why they have to be
/// written down. A price that is wrong is visible in the series; a membership
/// history that was never sourced looks exactly like an index that had no
/// constituents, and nothing in the data will ever say which it was.
/// </para>
/// <para>
/// Values are explicit because they are persisted and outlive this
/// declaration's order.
/// </para>
/// </remarks>
public enum UniverseCoverageFindingKind
{
    /// <summary>The universe is defined and has no membership rows at all.</summary>
    /// <remarks>
    /// The state a universe is in before anyone sources its history. Recorded
    /// rather than tolerated, because a defined universe with no membership is
    /// the exact shape a research query would read as an empty market.
    /// </remarks>
    NoMembershipRecorded = 1,

    /// <summary>
    /// The universe has membership rows and claims no span, so no date can be
    /// said to be known.
    /// </summary>
    /// <remarks>
    /// Worse than having nothing, in one specific way: the rows make the
    /// universe look sourced while the missing claim means every as-of read
    /// against it is unanswerable.
    /// </remarks>
    NoCoverageDeclared = 2,

    /// <summary>
    /// Membership is recorded for dates the universe does not claim to know.
    /// </summary>
    /// <remarks>
    /// The claim and the rows disagree. Either the history was sourced further
    /// back than the claim admits, or rows arrived from somewhere the claim
    /// does not account for — and until someone decides which, the claim cannot
    /// be trusted to describe what the rows contain.
    /// </remarks>
    MembershipOutsideCoverage = 3,
}

/// <summary>
/// A gap in a universe's membership history, recorded so that it cannot pass
/// for completeness.
/// </summary>
/// <remarks>
/// <para>
/// The read side already refuses to let an unsourced date look like an empty
/// set: it answers <em>unknown</em>. This is the other half — the gap stated
/// once, in a place an operator can look at without running a query for every
/// date, and kept open until something accounts for it.
/// </para>
/// <para>
/// One finding per universe and kind, enforced by the schema. A review that
/// runs nightly must not raise the same gap again, and a dismissal must not be
/// undone by the next run.
/// </para>
/// <para>
/// Status is <see cref="DataQualityIssueStatus"/>, the same three states a bar
/// quality finding uses. A second, parallel vocabulary for the same lifecycle
/// would eventually drift, and a dashboard would have to know both.
/// </para>
/// </remarks>
public sealed class UniverseCoverageFinding
{
    /// <summary>Longest permitted detail or resolution text.</summary>
    public const int MaxTextLength = 500;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private UniverseCoverageFinding() => Detail = null!;

    private UniverseCoverageFinding(
        UniverseCoverageFindingId id,
        UniverseId universeId,
        UniverseCoverageFindingKind kind,
        string detail,
        DateTimeOffset detectedAtUtc)
    {
        Id = id;
        UniverseId = universeId;
        Kind = kind;
        Detail = detail;
        DetectedAtUtc = detectedAtUtc;
        Status = DataQualityIssueStatus.Open;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public UniverseCoverageFindingId Id { get; private set; }

    /// <summary>Gets the universe the finding concerns.</summary>
    public UniverseId UniverseId { get; private set; }

    /// <summary>Gets what was found.</summary>
    public UniverseCoverageFindingKind Kind { get; private set; }

    /// <summary>Gets the specifics, including the numbers that triggered it.</summary>
    public string Detail { get; private set; }

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
    /// <param name="universeId">The universe concerned.</param>
    /// <param name="kind">What was found.</param>
    /// <param name="detail">The specifics.</param>
    /// <param name="detectedAtUtc">The instant it was found.</param>
    /// <returns>The new finding, open.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static UniverseCoverageFinding Raise(
        UniverseId universeId,
        UniverseCoverageFindingKind kind,
        string detail,
        DateTimeOffset detectedAtUtc)
    {
        if (universeId.IsEmpty)
        {
            throw new DomainValidationException("A coverage finding must concern a universe.");
        }

        if (detectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Audit timestamps must be UTC, but the offset was {detectedAtUtc.Offset}.");
        }

        return new UniverseCoverageFinding(
            UniverseCoverageFindingId.New(),
            universeId,
            kind,
            RequireText(detail, "A coverage finding must say what was found."),
            detectedAtUtc);
    }

    /// <summary>
    /// Records that something known accounts for the finding.
    /// </summary>
    /// <remarks>
    /// What a later review does when the gap has been filled: history was
    /// sourced, or a claim was declared. The finding is closed rather than
    /// deleted, so the record still shows that the universe was once
    /// incomplete and when it stopped being.
    /// </remarks>
    /// <param name="resolution">What accounts for it.</param>
    /// <param name="resolvedAtUtc">The instant it was accounted for.</param>
    /// <exception cref="DomainStateException">The finding is not open.</exception>
    public void Explain(string resolution, DateTimeOffset resolvedAtUtc) =>
        Close(DataQualityIssueStatus.Explained, resolution, resolvedAtUtc);

    /// <summary>
    /// Records that the finding was investigated and is not a problem.
    /// </summary>
    /// <remarks>
    /// A custom watchlist an operator keeps empty on purpose is the ordinary
    /// case. A dismissal survives later reviews, which is the point: the same
    /// gap must not come back every night once somebody has ruled on it.
    /// </remarks>
    /// <param name="reason">Why it is not a problem.</param>
    /// <param name="resolvedAtUtc">The instant it was dismissed.</param>
    /// <exception cref="DomainStateException">The finding is not open.</exception>
    public void Dismiss(string reason, DateTimeOffset resolvedAtUtc) =>
        Close(DataQualityIssueStatus.Dismissed, reason, resolvedAtUtc);

    private void Close(DataQualityIssueStatus status, string text, DateTimeOffset resolvedAtUtc)
    {
        if (!IsOpen)
        {
            // Overwriting a resolution would erase the audit trail the finding
            // exists to leave.
            throw new DomainStateException(
                $"This finding is already {Status} and cannot be closed again.");
        }

        if (resolvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Audit timestamps must be UTC, but the offset was {resolvedAtUtc.Offset}.");
        }

        if (resolvedAtUtc < DetectedAtUtc)
        {
            throw new DomainValidationException(
                $"A resolution at {resolvedAtUtc:O} predates the finding at {DetectedAtUtc:O}.");
        }

        Status = status;
        Resolution = RequireText(text, "Closing a finding must say what accounted for it.");
        ResolvedAtUtc = resolvedAtUtc;
    }

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException(message);
        }

        var trimmed = value.Trim();

        return trimmed.Length <= MaxTextLength
            ? trimmed
            : throw new DomainValidationException(
                $"The text may be at most {MaxTextLength} characters.");
    }
}
