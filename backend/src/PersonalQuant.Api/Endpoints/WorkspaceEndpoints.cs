using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Terminal;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// The operator's saved workspaces (ADR-026 slice 5), on the write path
/// ADR-027 set up for watchlists.
/// </summary>
internal static class WorkspaceEndpoints
{
    private const string RouteGroup = "/workspaces";
    private const string OpenApiTag = "Workspaces";

    /// <summary>Maps the workspace endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var reads = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag);

        reads.MapGet("/", ListAsync)
            .WithName("ListWorkspaces")
            .WithSummary("Lists every saved workspace.");

        reads.MapGet("/{name}", GetAsync)
            .WithName("GetWorkspace")
            .WithSummary("Reads one saved workspace.");

        var writes = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag).RequireOperatorKey();

        // PUT, because saving under a name is an upsert: saving the same lines
        // twice leaves the workspace as saving once would.
        writes.MapPut("/{name}", SaveAsync)
            .WithName("SaveWorkspace")
            .WithSummary("Saves a workspace, creating it or replacing its lines. Requires the operator key.");

        writes.MapDelete("/{name}", DeleteAsync)
            .WithName("DeleteWorkspace")
            .WithSummary("Deletes a workspace. Requires the operator key.");

        return endpoints;
    }

    private static async Task<Ok<WorkspaceListResponse>> ListAsync(
        IWorkspaceService workspaces,
        CancellationToken cancellationToken)
    {
        var held = await workspaces.ListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new WorkspaceListResponse([.. held.Select(WorkspaceResponse.From)]));
    }

    private static async Task<Results<Ok<WorkspaceResponse>, ProblemHttpResult>> GetAsync(
        string name,
        IWorkspaceService workspaces,
        CancellationToken cancellationToken)
    {
        var view = await workspaces.FindAsync(name, cancellationToken).ConfigureAwait(false);

        return view is null
            ? TypedResults.Problem(
                detail: $"There is no workspace named '{name}'.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Workspace not found.")
            : TypedResults.Ok(WorkspaceResponse.From(view));
    }

    private static async Task<Results<Ok<WorkspaceResponse>, Created<WorkspaceResponse>, ProblemHttpResult>> SaveAsync(
        string name,
        SaveWorkspaceRequest request,
        IWorkspaceService workspaces,
        CancellationToken cancellationToken)
    {
        var change = await workspaces
            .SaveAsync(name, request?.Entries ?? [], cancellationToken)
            .ConfigureAwait(false);

        return change switch
        {
            { Outcome: WorkspaceOutcome.Created, Workspace: { } created } =>
                TypedResults.Created($"{RouteGroup}/{created.Name}", WorkspaceResponse.From(created)),
            { Outcome: WorkspaceOutcome.Replaced, Workspace: { } replaced } =>
                TypedResults.Ok(WorkspaceResponse.From(replaced)),
            _ => TypedResults.Problem(
                detail: change.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The workspace request is not valid."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        IWorkspaceService workspaces,
        CancellationToken cancellationToken)
    {
        var change = await workspaces.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        return change.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                detail: change.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Workspace not found.");
    }
}
