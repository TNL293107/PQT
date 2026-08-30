using System.Globalization;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// What a coverage review did.
/// </summary>
/// <param name="Reviewed">How many universes were examined.</param>
/// <param name="Raised">How many findings were newly recorded.</param>
/// <param name="Explained">How many standing findings the review closed.</param>
/// <param name="StillOpen">How many findings remain open after it.</param>
public sealed record UniverseCoverageReport(int Reviewed, int Raised, int Explained, int StillOpen);

/// <summary>
/// Records the gaps in every universe's membership history.
/// </summary>
/// <remarks>
/// <para>
/// The read side already answers <em>unknown</em> for a date nobody sourced, so
/// no single query can be fooled. This exists because nobody runs that query
/// for every date: without a recorded finding, an unsourced universe is silent
/// until a researcher happens to ask about the one year that is missing.
/// </para>
/// <para>
/// Findings are staged, never committed here. The caller owns the transaction,
/// which is what lets an import record membership and the finding about it
/// together.
/// </para>
/// </remarks>
public interface IUniverseCoverageReview
{
    /// <summary>
    /// Reviews every universe and records what is missing.
    /// </summary>
    /// <remarks>
    /// Idempotent. A gap already recorded is left alone, so a nightly run
    /// raises nothing new and does not undo yesterday's dismissal; a gap that
    /// has since been filled is explained rather than deleted.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the review found.</returns>
    Task<UniverseCoverageReport> ReviewAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUniverseCoverageReview" />
/// <param name="universes">The universe store.</param>
/// <param name="clock">The clock that stamps a finding.</param>
public sealed class UniverseCoverageReview(IUniverseRepository universes, IClock clock)
    : IUniverseCoverageReview
{
    /// <inheritdoc />
    public async Task<UniverseCoverageReport> ReviewAsync(
        CancellationToken cancellationToken = default)
    {
        var detectedAtUtc = clock.UtcNow;
        var defined = await universes.ListAsync(cancellationToken).ConfigureAwait(false);

        var raised = 0;
        var explained = 0;
        var stillOpen = 0;

        foreach (var universe in defined)
        {
            var span = await universes
                .DescribeMembershipAsync(universe.Id, cancellationToken)
                .ConfigureAwait(false);

            var found = Diagnose(universe, span);

            var open = await universes
                .ListOpenFindingsAsync(universe.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var (kind, detail) in found)
            {
                if (open.Any(finding => finding.Kind == kind))
                {
                    // Already recorded. Re-raising it would reset its age and
                    // bury whichever one an operator had already looked at.
                    continue;
                }

                universes.Add(UniverseCoverageFinding.Raise(
                    universe.Id, kind, detail, detectedAtUtc));
                raised++;
                stillOpen++;
            }

            foreach (var finding in open)
            {
                if (found.Any(gap => gap.Kind == finding.Kind))
                {
                    stillOpen++;
                    continue;
                }

                // The gap has been filled. Closed rather than deleted, so the
                // record still shows that the universe was incomplete and when
                // it stopped being.
                finding.Explain("The gap is no longer present.", detectedAtUtc);
                explained++;
            }
        }

        return new UniverseCoverageReport(defined.Count, raised, explained, stillOpen);
    }

    /// <summary>
    /// Decides which gaps a universe currently has.
    /// </summary>
    /// <remarks>
    /// Pure, and separated from the staging above so that the rules can be
    /// tested without a store. Each kind is raised for a distinct, actionable
    /// reason; a universe with no rows is not also told that it has declared no
    /// claim, because filling the rows is the only useful next step either way.
    /// </remarks>
    /// <param name="universe">The universe examined.</param>
    /// <param name="span">What its rows actually span.</param>
    /// <returns>The gaps found, each with the detail to record.</returns>
    public static IReadOnlyList<(UniverseCoverageFindingKind Kind, string Detail)> Diagnose(
        Universe universe,
        UniverseMembershipSpan span)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(span);

        var found = new List<(UniverseCoverageFindingKind, string)>();

        if (span.IsEmpty)
        {
            found.Add((
                UniverseCoverageFindingKind.NoMembershipRecorded,
                $"{universe.Code} is defined and has no membership recorded. "
                + "Every as-of read against it is unknown, not empty."));

            return found;
        }

        if (universe.Coverage is not { } coverage)
        {
            found.Add((
                UniverseCoverageFindingKind.NoCoverageDeclared,
                $"{universe.Code} has {span.Count.ToString(CultureInfo.InvariantCulture)} membership "
                + "spells and claims no span, so no date can be said to be known."));

            return found;
        }

        var outside = DescribeOutsideCoverage(coverage, span);

        if (outside is not null)
        {
            found.Add((UniverseCoverageFindingKind.MembershipOutsideCoverage, outside));
        }

        return found;
    }

    private static string? DescribeOutsideCoverage(
        MembershipCoverage coverage,
        UniverseMembershipSpan span)
    {
        if (span.EarliestFrom is { } earliest && earliest < coverage.From)
        {
            return $"Membership begins {earliest:O}, before the claimed span opens {coverage.From:O}.";
        }

        if (coverage.Until is not { } until)
        {
            // An open claim reaches everything the rows can.
            return null;
        }

        if (span.HasOpenSpell)
        {
            return $"A spell is still running past the claimed span, which closes {until:O}.";
        }

        return span.LatestEnd is { } latest && latest > until
            ? $"Membership runs to {latest:O}, past the claimed span closing {until:O}."
            : null;
    }
}
