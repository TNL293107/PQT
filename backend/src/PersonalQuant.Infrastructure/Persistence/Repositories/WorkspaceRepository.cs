using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IWorkspaceRepository"/>.</summary>
internal sealed class WorkspaceRepository(PersonalQuantDbContext dbContext) : IWorkspaceRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Workspaces
            .AsNoTracking()
            .OrderBy(workspace => workspace.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Workspace?> FindAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Workspaces.FirstOrDefaultAsync(workspace => workspace.Name == name, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        dbContext.Workspaces.CountAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Workspace workspace) => dbContext.Workspaces.Add(workspace);

    /// <inheritdoc />
    public void Remove(Workspace workspace) => dbContext.Workspaces.Remove(workspace);
}
