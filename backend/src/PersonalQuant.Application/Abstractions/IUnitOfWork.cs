namespace PersonalQuant.Application.Abstractions;

/// <summary>
/// Commits the changes tracked during a single unit of work.
/// </summary>
/// <remarks>
/// Persistence is deliberately separated from the repositories: registering an
/// instrument and recording the exchange it transferred to must land in one
/// transaction, and a repository that saved on every call could not offer
/// that.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists every pending change.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
