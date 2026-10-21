using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICorporateActionCoverageRepository"/>.
/// </summary>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class CorporateActionCoverageRepository(PersonalQuantDbContext dbContext) : ICorporateActionCoverageRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCoverage>> ListAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.CorporateActionCoverage
            .AsNoTracking()
            .Where(coverage => coverage.InstrumentId == instrumentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(CorporateActionCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        dbContext.CorporateActionCoverage.Add(coverage);
    }
}
