using PersonalQuant.Domain.Classification;

namespace PersonalQuant.Application.Classification;

/// <summary>
/// Reads and records the sector and industry taxonomy.
/// </summary>
/// <remarks>
/// <para>
/// One port for both levels. They are always read together — an industry
/// without its sector is half an answer — and splitting them would only give
/// two objects that are never used apart.
/// </para>
/// <para>
/// There is no delete, for the same reason the instrument master has none.
/// Instruments classified under a node continue to reference it, and a
/// taxonomy that revises itself issues new nodes rather than removing old
/// ones.
/// </para>
/// </remarks>
public interface IClassificationRepository
{
    /// <summary>Finds a sector by its taxonomy code.</summary>
    /// <param name="code">The code to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The sector, or <see langword="null"/> when unknown.</returns>
    Task<Sector?> FindSectorByCodeAsync(
        ClassificationCode code,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every sector, ordered by code.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All sectors.</returns>
    Task<IReadOnlyList<Sector>> ListSectorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds an industry by its taxonomy code.</summary>
    /// <param name="code">The code to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The industry, or <see langword="null"/> when unknown.</returns>
    Task<Industry?> FindIndustryByCodeAsync(
        ClassificationCode code,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every industry, ordered by code.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All industries.</returns>
    Task<IReadOnlyList<Industry>> ListIndustriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new sector. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="sector">The sector to add.</param>
    void AddSector(Sector sector);

    /// <summary>
    /// Stages a new industry. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="industry">The industry to add.</param>
    void AddIndustry(Industry industry);
}
