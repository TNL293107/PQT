using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Classification;
using PersonalQuant.Domain.Classification;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IClassificationRepository"/>.
/// </summary>
/// <remarks>
/// The taxonomy is small — tens of rows that change on the scale of years —
/// so every read here is an unpaginated list or a single lookup by code, and
/// deliberately so. Anything that needs it per instrument joins to it in the
/// instrument query rather than fetching it here row by row.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class ClassificationRepository(PersonalQuantDbContext dbContext)
    : IClassificationRepository
{
    /// <inheritdoc />
    public Task<Sector?> FindSectorByCodeAsync(
        ClassificationCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        return dbContext.Sectors.FirstOrDefaultAsync(
            sector => sector.Code == code,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sector>> ListSectorsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Sectors
            .AsNoTracking()
            .OrderBy(sector => sector.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Industry?> FindIndustryByCodeAsync(
        ClassificationCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        return dbContext.Industries.FirstOrDefaultAsync(
            industry => industry.Code == code,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Industry>> ListIndustriesAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Industries
            .AsNoTracking()
            .OrderBy(industry => industry.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddSector(Sector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        dbContext.Sectors.Add(sector);
    }

    /// <inheritdoc />
    public void AddIndustry(Industry industry)
    {
        ArgumentNullException.ThrowIfNull(industry);

        dbContext.Industries.Add(industry);
    }
}
