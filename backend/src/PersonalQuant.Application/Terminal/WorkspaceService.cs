using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.Application.Terminal;

/// <summary>Reads and writes workspaces.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Lists every workspace, by name.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The workspaces.</returns>
    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a workspace, tracked for change.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The workspace, or <see langword="null"/> when there is none by that name.</returns>
    Task<Workspace?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Counts the workspaces held.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages a new workspace.</summary>
    /// <param name="workspace">The workspace.</param>
    void Add(Workspace workspace);

    /// <summary>Stages a workspace's deletion.</summary>
    /// <param name="workspace">The workspace, as loaded by <see cref="FindAsync"/>.</param>
    void Remove(Workspace workspace);
}

/// <summary>A saved workspace, as a reader sees it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Entries">The console lines, in replay order.</param>
/// <param name="CreatedAtUtc">When it was first saved.</param>
/// <param name="UpdatedAtUtc">When it was last saved.</param>
public sealed record WorkspaceView(
    string Name,
    IReadOnlyList<string> Entries,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Reads a workspace into its view.</summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>The view.</returns>
    public static WorkspaceView From(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return new WorkspaceView(workspace.Name, [.. workspace.Entries], workspace.CreatedAtUtc, workspace.UpdatedAtUtc);
    }
}

/// <summary>How saving or deleting a workspace ended.</summary>
public enum WorkspaceOutcome
{
    /// <summary>A new workspace was saved.</summary>
    Created = 1,

    /// <summary>An existing workspace's lines were replaced.</summary>
    Replaced = 2,

    /// <summary>The workspace was deleted.</summary>
    Deleted = 3,

    /// <summary>There is no workspace by that name.</summary>
    NotFound = 4,

    /// <summary>The request breaks a rule.</summary>
    Invalid = 5,

    /// <summary>A concurrent save created the workspace first; saving again replaces it.</summary>
    Conflict = 6,
}

/// <summary>What saving or deleting a workspace did.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Message">Why, when it was refused.</param>
/// <param name="Workspace">The workspace as it now stands, when it exists.</param>
public sealed record WorkspaceChange(WorkspaceOutcome Outcome, string? Message, WorkspaceView? Workspace)
{
    /// <summary>Gets a value indicating whether the change was made.</summary>
    public bool Succeeded => Outcome is WorkspaceOutcome.Created or WorkspaceOutcome.Replaced or WorkspaceOutcome.Deleted;
}

/// <summary>The operator's saved workspaces (ADR-026 slice 5).</summary>
public interface IWorkspaceService
{
    /// <summary>Lists every workspace.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The workspaces, by name.</returns>
    Task<IReadOnlyList<WorkspaceView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one workspace.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The workspace, or <see langword="null"/> when there is none by that name.</returns>
    Task<WorkspaceView?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves a workspace, creating it or replacing its lines.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="entries">The console lines, in replay order.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WorkspaceChange> SaveAsync(
        string name,
        IReadOnlyList<string> entries,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a workspace.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WorkspaceChange> DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>The one implementation of <see cref="IWorkspaceService"/>.</summary>
/// <remarks>
/// Saving is an upsert by name. A workspace is something the operator
/// overwrites with "save morning" as their routine changes, and making them
/// delete it first would be ceremony with no protection behind it.
/// </remarks>
internal sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
    IUnitOfWork unitOfWork,
    IClock clock) : IWorkspaceService
{
    /// <summary>The most workspaces a deployment may hold.</summary>
    public const int MaxWorkspaces = 100;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var held = await workspaces.ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. held.Select(WorkspaceView.From)];
    }

    /// <inheritdoc />
    public async Task<WorkspaceView?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!Watchlist.IsValidName(name))
        {
            return null;
        }

        var workspace = await workspaces.FindAsync(name, cancellationToken).ConfigureAwait(false);

        return workspace is null ? null : WorkspaceView.From(workspace);
    }

    /// <inheritdoc />
    public async Task<WorkspaceChange> SaveAsync(
        string name,
        IReadOnlyList<string> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var existing = Watchlist.IsValidName(name)
            ? await workspaces.FindAsync(name, cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            if (existing is not null)
            {
                existing.Replace(entries, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return new WorkspaceChange(WorkspaceOutcome.Replaced, null, WorkspaceView.From(existing));
            }

            if (await workspaces.CountAsync(cancellationToken).ConfigureAwait(false) >= MaxWorkspaces)
            {
                return new WorkspaceChange(
                    WorkspaceOutcome.Invalid,
                    $"This deployment already holds {MaxWorkspaces} workspaces, the most it keeps.",
                    null);
            }

            var created = Workspace.Create(name, entries, clock.UtcNow);
            workspaces.Add(created);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (UniqueConstraintException)
            {
                return new WorkspaceChange(
                    WorkspaceOutcome.Conflict,
                    $"Workspace '{name}' was created by another request at the same moment. Save again to replace it.",
                    null);
            }

            return new WorkspaceChange(WorkspaceOutcome.Created, null, WorkspaceView.From(created));
        }
        catch (DomainValidationException exception)
        {
            return new WorkspaceChange(WorkspaceOutcome.Invalid, exception.Message, null);
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceChange> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var workspace = Watchlist.IsValidName(name)
            ? await workspaces.FindAsync(name, cancellationToken).ConfigureAwait(false)
            : null;

        if (workspace is null)
        {
            return new WorkspaceChange(WorkspaceOutcome.NotFound, $"There is no workspace named '{name}'.", null);
        }

        workspaces.Remove(workspace);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new WorkspaceChange(WorkspaceOutcome.Deleted, null, null);
    }
}
