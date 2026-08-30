using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// Default <see cref="IUniverseImportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves each row's symbol the way the corporate action import does — the
/// provider's own spelling, and no fallback to a bare ticker. A ticker live on
/// two venues would otherwise attach a review notice to the wrong security, and
/// a membership attached to the wrong security is worse than a rejected row:
/// it silently changes what a strategy could have chosen from.
/// </para>
/// <para>
/// The whole run commits once, and the coverage review runs inside the same
/// transaction. Membership committed without the review would leave a universe
/// looking sourced for a moment nobody can reconstruct afterwards — and the
/// findings are the only record that says which universes are incomplete.
/// </para>
/// <para>
/// Nothing is deleted. A source that has stopped publishing a spell may have
/// shortened its window rather than corrected itself, and inferring a removal
/// from an absence would rewrite history that a backtest has already run
/// against.
/// </para>
/// </remarks>
/// <param name="providers">Every registered universe source.</param>
/// <param name="instruments">Resolves a source's symbol to an instrument.</param>
/// <param name="universes">The universe record.</param>
/// <param name="review">Records what is still missing afterwards.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
/// <param name="logger">Logger for import telemetry.</param>
internal sealed class UniverseImportService(
    IEnumerable<IUniverseMembershipProvider> providers,
    IInstrumentRepository instruments,
    IUniverseRepository universes,
    IUniverseCoverageReview review,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<UniverseImportService> logger) : IUniverseImportService
{
    /// <inheritdoc />
    public async Task<UniverseImportReport> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        var registered = providers.ToList();

        var provider = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                registered.Count == 0
                    ? "No universe source is registered."
                    : "Several universe sources are registered, which is not supported.");

        var occurredAtUtc = clock.UtcNow;

        var defined = await provider.ListUniversesAsync(cancellationToken).ConfigureAwait(false);
        var known = await DefineAsync(defined, provider.Code, occurredAtUtc, cancellationToken)
            .ConfigureAwait(false);

        var rows = await provider.ListMembershipsAsync(cancellationToken).ConfigureAwait(false);

        var state = new ImportState();

        foreach (var row in rows)
        {
            await ApplyAsync(row, known.Universes, provider.Code, occurredAtUtc, state, cancellationToken)
                .ConfigureAwait(false);
        }

        // Inside the same unit of work as the membership above, so that the
        // rows and the record of what is still missing from them commit
        // together or not at all.
        var coverage = await review.ReviewAsync(cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var report = new UniverseImportReport(
            provider.Code.Value,
            known.Defined,
            known.CoverageDeclared,
            rows.Count,
            state.Created,
            state.Closed,
            state.Unchanged,
            state.Rejections,
            coverage);

        ApplicationLog.UniverseMembershipImported(
            logger,
            report.Source,
            report.RowsRead,
            report.SpellsCreated,
            report.SpellsClosed,
            report.Unchanged,
            report.Rejected,
            coverage.StillOpen);

        return report;
    }

    /// <summary>
    /// Records the universes the source defines and refreshes their coverage
    /// claims.
    /// </summary>
    /// <remarks>
    /// A universe already held keeps its name and kind. Only the claim is
    /// refreshed, because the claim is the one thing an import genuinely
    /// restates: sourcing older history widens it. Renaming a universe from a
    /// file would let a typo in one row detach every membership joined to it
    /// from the name a manifest recorded.
    /// </remarks>
    private async Task<(Dictionary<string, Universe> Universes, int Defined, int CoverageDeclared)>
        DefineAsync(
            IReadOnlyList<ProviderUniverse> defined,
            SourceCode source,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
    {
        var known = new Dictionary<string, Universe>(StringComparer.Ordinal);
        var created = 0;
        var declared = 0;

        foreach (var row in defined)
        {
            if (!UniverseCode.TryCreate(row.Code, out var code)
                || !TryReadKind(row.Kind, out var kind))
            {
                continue;
            }

            // A file that names one universe twice must not stage it twice; the
            // second row would collide on the unique code and fail the run.
            if (!known.TryGetValue(code.Value, out var universe))
            {
                universe = await universes.FindByCodeAsync(code, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (universe is null)
            {
                universe = Universe.Define(
                    UniverseId.New(), code, row.Name, kind, source, occurredAtUtc);

                universes.Add(universe);
                created++;
            }

            if (TryReadCoverage(row, out var coverage) && !coverage.Equals(universe.Coverage))
            {
                universe.DeclareCoverage(coverage, source, occurredAtUtc);
                declared++;
            }

            known[code.Value] = universe;
        }

        return (known, created, declared);
    }

    private async Task ApplyAsync(
        ProviderUniverseMembership row,
        Dictionary<string, Universe> known,
        SourceCode source,
        DateTimeOffset occurredAtUtc,
        ImportState state,
        CancellationToken cancellationToken)
    {
        if (!UniverseCode.TryCreate(row.UniverseCode, out var code)
            || !known.TryGetValue(code.Value, out var universe))
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.UnknownUniverse,
                $"'{row.UniverseCode}' is not a universe this source defines.");

            return;
        }

        var instrumentId = await ResolveAsync(state.Resolved, row.Symbol, source, cancellationToken)
            .ConfigureAwait(false);

        if (instrumentId is null)
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.UnknownInstrument,
                $"'{row.Symbol}' does not resolve to an instrument this system holds.");

            return;
        }

        if (row.EffectiveTo is { } end && end <= row.EffectiveFrom)
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.EmptyInterval,
                $"A spell from {row.EffectiveFrom:yyyy-MM-dd} cannot end on {end:yyyy-MM-dd}.");

            return;
        }

        if (!state.Seen.Add((universe.Id, instrumentId.Value, row.EffectiveFrom)))
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.DuplicateWithinImport,
                $"A spell for '{row.Symbol}' from {row.EffectiveFrom:yyyy-MM-dd} appeared twice.");

            return;
        }

        var recorded = await universes
            .ListSpellsForUpdateAsync(universe.Id, instrumentId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Spells staged earlier in this same run are not in the database yet
        // and would not come back from that query. A file that admits a
        // security twice would then reach the exclusion constraint and take
        // the whole run's transaction with it, so both are checked here.
        var spells = recorded
            .Concat(state.Staged(universe.Id, instrumentId.Value))
            .ToList();

        var existing = spells.FirstOrDefault(spell => spell.EffectiveFrom == row.EffectiveFrom);

        if (existing is not null)
        {
            Reconcile(existing, row, state);
            return;
        }

        var candidate = UniverseMembership.Admit(
            universe.Id,
            instrumentId.Value,
            row.EffectiveFrom,
            row.AnnouncedOn,
            source,
            occurredAtUtc);

        if (row.EffectiveTo is { } closesAt)
        {
            candidate.Remove(closesAt);
        }

        // Checked here so the rejection can name the security and both spells.
        // The exclusion constraint is still what makes the rule true of the
        // table; this only makes the failure legible, and stops one bad row
        // from failing the whole run's transaction.
        var clash = spells.FirstOrDefault(spell => spell.Overlaps(candidate));

        if (clash is not null)
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.OverlapsRecordedSpell,
                $"'{row.Symbol}' already has a spell from {clash.EffectiveFrom:yyyy-MM-dd} "
                + $"covering {row.EffectiveFrom:yyyy-MM-dd}.");

            return;
        }

        universes.Add(candidate);
        state.Stage(candidate);
        state.Created++;
    }

    /// <summary>
    /// Reconciles a row against the spell already recorded for that start date.
    /// </summary>
    /// <remarks>
    /// Only one transition is allowed: an open spell the source now reports as
    /// ended. Everything else is refused. A spell already recorded as ended is
    /// a fact about the past that something may have already read, and a source
    /// changing its mind about one needs a decision rather than a silent
    /// rewrite.
    /// </remarks>
    private static void Reconcile(
        UniverseMembership existing,
        ProviderUniverseMembership row,
        ImportState state)
    {
        if (existing.EffectiveTo == row.EffectiveTo)
        {
            state.Unchanged++;
            return;
        }

        if (existing.EffectiveTo is not null)
        {
            state.Reject(
                row,
                UniverseMembershipRejectionReason.ContradictsRecordedSpell,
                $"'{row.Symbol}' is already recorded as leaving on "
                + $"{existing.EffectiveTo:yyyy-MM-dd}, and the source now says "
                + $"{(row.EffectiveTo is { } to ? to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "never")}.");

            return;
        }

        if (row.EffectiveTo is not { } closesAt)
        {
            // Open here, open there. Cannot happen — the equality above caught
            // it — but stated so the branch is not silently a fall-through.
            state.Unchanged++;
            return;
        }

        try
        {
            existing.Remove(closesAt);
            state.Closed++;
        }
        catch (DomainValidationException exception)
        {
            state.Reject(row, UniverseMembershipRejectionReason.EmptyInterval, exception.Message);
        }
    }

    private async Task<InstrumentId?> ResolveAsync(
        Dictionary<string, InstrumentId?> cache,
        string? symbol,
        SourceCode source,
        CancellationToken cancellationToken)
    {
        if (!ProviderSymbol.TryParse(symbol, out var parsed, out _))
        {
            return null;
        }

        if (cache.TryGetValue(parsed.Raw, out var cached))
        {
            return cached;
        }

        InstrumentId? found = null;

        if (IdentifierValue.TryCreate(
                IdentifierScheme.ProviderSymbol, parsed.Raw, out var alias, out _))
        {
            var identifier = await instruments
                .FindIdentifierAsync(alias, source, cancellationToken)
                .ConfigureAwait(false);

            found = identifier?.InstrumentId;
        }

        cache[parsed.Raw] = found;
        return found;
    }

    private static bool TryReadKind(string? value, out UniverseKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && kind.IsDeclared();

    private static bool TryReadCoverage(ProviderUniverse row, out MembershipCoverage coverage)
    {
        coverage = null!;

        if (row.CoverageFrom is not { } from)
        {
            // No claim stated. Left absent rather than guessed from the rows:
            // a claim the operator did not make is not a claim.
            return false;
        }

        if (row.CoverageUntil is { } until && until <= from)
        {
            return false;
        }

        coverage = MembershipCoverage.Create(from, row.CoverageUntil);
        return true;
    }

    private sealed class ImportState
    {
        public Dictionary<string, InstrumentId?> Resolved { get; } = new(StringComparer.Ordinal);

        public HashSet<(UniverseId Universe, InstrumentId Instrument, DateOnly From)> Seen { get; } =
            [];

        private Dictionary<(UniverseId Universe, InstrumentId Instrument), List<UniverseMembership>>
            StagedSpells { get; } = [];

        public List<UniverseMembershipRejection> Rejections { get; } = [];

        public int Created { get; set; }

        public int Closed { get; set; }

        public int Unchanged { get; set; }

        /// <summary>Records a spell this run has staged but not yet committed.</summary>
        public void Stage(UniverseMembership membership)
        {
            var key = (membership.UniverseId, membership.InstrumentId);

            if (!StagedSpells.TryGetValue(key, out var spells))
            {
                spells = [];
                StagedSpells[key] = spells;
            }

            spells.Add(membership);
        }

        /// <summary>Gets the spells this run has staged for one security.</summary>
        public List<UniverseMembership> Staged(
            UniverseId universeId,
            InstrumentId instrumentId) =>
            StagedSpells.TryGetValue((universeId, instrumentId), out var spells) ? spells : [];

        public void Reject(
            ProviderUniverseMembership row,
            UniverseMembershipRejectionReason reason,
            string detail) =>
            Rejections.Add(new UniverseMembershipRejection(row, reason, detail));
    }
}
