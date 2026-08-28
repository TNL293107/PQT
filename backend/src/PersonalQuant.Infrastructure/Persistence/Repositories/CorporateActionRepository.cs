using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICorporateActionRepository"/>.
/// </summary>
/// <remarks>
/// Actions and their factors are read tracked, because both are written back:
/// an import amends an action, and a recompute replaces the factor derived from
/// it. Reading them detached would compute the right answer and never store it.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class CorporateActionRepository(PersonalQuantDbContext dbContext)
    : ICorporateActionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateAction>> ListAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.CorporateActions
            .Where(action => action.InstrumentId == instrumentId)
            // Total, so two actions sharing an ex-date are recomputed in the
            // same order every time and the run's counts are reproducible.
            .OrderBy(action => action.ExDate)
            .ThenBy(action => action.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<CorporateAction?> FindAsync(
        InstrumentId instrumentId,
        CorporateActionType type,
        DateOnly exDate,
        CancellationToken cancellationToken = default) =>
        dbContext.CorporateActions.FirstOrDefaultAsync(
            action =>
                action.InstrumentId == instrumentId
                && action.Type == type
                && action.ExDate == exDate,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceAdjustment>> ListAdjustmentsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.PriceAdjustments
            .Where(adjustment => adjustment.InstrumentId == instrumentId)
            .OrderBy(adjustment => adjustment.ExDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(CorporateAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        dbContext.CorporateActions.Add(action);
    }

    /// <inheritdoc />
    public void AddAdjustment(PriceAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        dbContext.PriceAdjustments.Add(adjustment);
    }

    /// <inheritdoc />
    public void RemoveAdjustment(PriceAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        dbContext.PriceAdjustments.Remove(adjustment);
    }
}
