using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// The span of dates a venue's trading calendar claims to have been
/// transcribed for.
/// </summary>
/// <remarks>
/// <para>
/// This exists because closure rows cannot answer the question that matters
/// most about them, and the attempt to make them do so was wrong in both
/// directions.
/// </para>
/// <para>
/// Coverage used to be inferred as <em>the furthest recorded closure</em>. Under
/// that rule a calendar transcribed for 2022–2026 reported its horizon as 2
/// September 2026 — the last public holiday of the final year — so every date
/// from October onwards read as uncovered and completeness for the last third
/// of the year became unmeasurable, correctly and silently, while the
/// transcription for it sat in the table. That is the under-claim.
/// </para>
/// <para>
/// The over-claim is worse and was live. The same rule made every date
/// <em>before</em> the furthest closure look covered, including years nobody had
/// transcribed at all — so a series ingested for 2016 was checked against a
/// calendar holding no 2016 closures, and three real Vietnamese public holidays
/// were raised as missing sessions. A finding that says the data is wrong when
/// the calendar is the thing that is missing costs more than no finding.
/// </para>
/// <para>
/// So the claim is recorded rather than derived, exactly as
/// <see cref="Universes.MembershipCoverage"/> is for universe membership. The
/// two are the same idea about different facts — a span somebody asserts, kept
/// apart from the rows that fill it — and they are written separately rather
/// than shared because a common abstraction over two instances would be named
/// after neither. A third occurrence is the point at which that changes.
/// </para>
/// <para>
/// Half-open, <c>[From, Until)</c>, like every other interval in this system.
/// </para>
/// </remarks>
public sealed record CalendarCoverage
{
    private CalendarCoverage(DateOnly from, DateOnly? until)
    {
        From = from;
        Until = until;
    }

    /// <summary>Gets the first date the calendar was transcribed for. Inclusive.</summary>
    public DateOnly From { get; }

    /// <summary>
    /// Gets the first date the calendar was <em>not</em> transcribed for, or
    /// <see langword="null"/> while the transcription is still being extended.
    /// Exclusive.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means the claim runs on, not that its end is
    /// unknown. Vietnam's calendar cannot run on: Tet is lunar and substitute
    /// days are set by annual decree, so the schedule exists only once a notice
    /// is published and transcribed. A Vietnamese venue that leaves this null is
    /// claiming to know a future nobody has announced.
    /// </remarks>
    public DateOnly? Until { get; }

    /// <summary>
    /// Creates a coverage claim.
    /// </summary>
    /// <param name="from">The first date transcribed.</param>
    /// <param name="until">The first date not transcribed, or null.</param>
    /// <returns>The claim.</returns>
    /// <exception cref="DomainValidationException">
    /// The span ends before it starts, or covers no date at all.
    /// </exception>
    public static CalendarCoverage Create(DateOnly from, DateOnly? until)
    {
        if (until is { } end && end <= from)
        {
            // [d, d) is empty, and a claim to have transcribed an empty span is
            // a claim to have transcribed nothing. Declaring no claim says that
            // unambiguously; a stored empty span is a value every reader has to
            // remember to special-case.
            throw new DomainValidationException(
                $"A calendar coverage claim must cover at least one date, but {from:O} to {end:O} covers none.");
        }

        return new CalendarCoverage(from, until);
    }

    /// <summary>
    /// Reports whether the claim covers a date.
    /// </summary>
    /// <param name="date">The date to test.</param>
    /// <returns><see langword="true"/> when the calendar was transcribed for it.</returns>
    public bool Covers(DateOnly date) =>
        date >= From && (Until is not { } until || date < until);

    /// <summary>
    /// Reports whether the claim covers an entire inclusive range.
    /// </summary>
    /// <remarks>
    /// Both ends, because a completeness figure computed over a window that is
    /// only partly transcribed is wrong for the part that is not, and there is
    /// nothing in the number that says which part.
    /// </remarks>
    /// <param name="fromDate">The first date of the window.</param>
    /// <param name="toDate">The last date of the window, inclusive.</param>
    /// <returns><see langword="true"/> when every date in the window is claimed.</returns>
    public bool CoversRange(DateOnly fromDate, DateOnly toDate) =>
        Covers(fromDate) && Covers(toDate);

    /// <summary>
    /// Gets the last date the claim covers, or <see langword="null"/> when it
    /// runs on.
    /// </summary>
    /// <remarks>
    /// The half-open end read as a date an operator can compare against today.
    /// </remarks>
    public DateOnly? Through => Until?.AddDays(-1);

    /// <inheritdoc />
    public override string ToString() =>
        Until is { } until ? $"[{From:O}, {until:O})" : $"[{From:O}, …)";
}
