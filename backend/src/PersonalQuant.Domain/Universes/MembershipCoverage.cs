using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Universes;

/// <summary>
/// The span of dates a universe claims to know its own membership for.
/// </summary>
/// <remarks>
/// <para>
/// This exists because membership rows cannot answer the question that matters
/// most about them. A universe with no rows for 2018 could mean the index had
/// no constituents that year, or that nobody has sourced them — and those are
/// opposite answers. Without a recorded claim, a query for 2018 returns an
/// empty set for both, and a backtest reads the absence of data as a fact about
/// the market.
/// </para>
/// <para>
/// The claim is deliberately separate from the rows and is not derived from
/// them. Deriving it from <c>MIN(effective_from)</c> would make a universe
/// sourced with a hole in the middle look continuously known, which is the same
/// silence in a different shape.
/// </para>
/// <para>
/// Half-open, <c>[From, Until)</c>, like every other interval in this system.
/// </para>
/// </remarks>
public sealed record MembershipCoverage
{
    private MembershipCoverage(DateOnly from, DateOnly? until)
    {
        From = from;
        Until = until;
    }

    /// <summary>Gets the first date whose membership is claimed to be known. Inclusive.</summary>
    public DateOnly From { get; }

    /// <summary>
    /// Gets the first date whose membership is <em>not</em> claimed to be
    /// known, or <see langword="null"/> while the universe is still being
    /// maintained. Exclusive.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means the claim runs on, not that its end is
    /// unknown. A universe whose upkeep has stopped states the date it stopped,
    /// so that a read past it is unknown rather than quietly stale.
    /// </remarks>
    public DateOnly? Until { get; }

    /// <summary>
    /// Creates a coverage claim.
    /// </summary>
    /// <param name="from">The first date claimed to be known.</param>
    /// <param name="until">The first date no longer claimed, or null.</param>
    /// <returns>The claim.</returns>
    /// <exception cref="DomainValidationException">
    /// The span ends before it starts, or covers no date at all.
    /// </exception>
    public static MembershipCoverage Create(DateOnly from, DateOnly? until)
    {
        if (until is { } end && end <= from)
        {
            // [d, d) is empty, and a claim to know an empty span is a claim to
            // know nothing. Saying so by claiming nothing is unambiguous; a
            // stored empty span is a value every reader has to remember to
            // special-case.
            throw new DomainValidationException(
                $"A coverage claim must cover at least one date, but {from:O} to {end:O} covers none.");
        }

        return new MembershipCoverage(from, until);
    }

    /// <summary>
    /// Reports whether the claim covers a date.
    /// </summary>
    /// <param name="asOf">The date to test.</param>
    /// <returns><see langword="true"/> when membership is claimed to be known then.</returns>
    public bool Covers(DateOnly asOf) =>
        From <= asOf && (Until is not { } end || asOf < end);
}
