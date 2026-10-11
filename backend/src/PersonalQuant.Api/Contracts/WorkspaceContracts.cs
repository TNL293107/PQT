using PersonalQuant.Application.Terminal;

namespace PersonalQuant.Api.Contracts;

/// <summary>A saved workspace.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Entries">The console lines, in replay order.</param>
/// <param name="CreatedAtUtc">When it was first saved.</param>
/// <param name="UpdatedAtUtc">When it was last saved.</param>
public sealed record WorkspaceResponse(
    string Name,
    IReadOnlyList<string> Entries,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Maps the application's view onto the wire contract.</summary>
    /// <param name="view">The view.</param>
    /// <returns>The response.</returns>
    public static WorkspaceResponse From(WorkspaceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new WorkspaceResponse(view.Name, view.Entries, view.CreatedAtUtc, view.UpdatedAtUtc);
    }
}

/// <summary>Every workspace.</summary>
/// <param name="Results">The workspaces, by name.</param>
public sealed record WorkspaceListResponse(IReadOnlyList<WorkspaceResponse> Results);

/// <summary>Saves a workspace's lines.</summary>
/// <param name="Entries">The console lines, in replay order.</param>
public sealed record SaveWorkspaceRequest(IReadOnlyList<string>? Entries);
