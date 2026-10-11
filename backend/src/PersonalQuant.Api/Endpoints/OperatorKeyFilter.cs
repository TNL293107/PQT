using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using PersonalQuant.Api.Configuration;

namespace PersonalQuant.Api.Endpoints;

/// <summary>Marks an endpoint as guarded by the operator key.</summary>
/// <remarks>
/// Metadata as well as a filter, so a test can enumerate every write endpoint
/// and prove none was mapped without the guard (ADR-027).
/// </remarks>
internal sealed class OperatorKeyRequirement
{
    /// <summary>The single instance attached to guarded endpoints.</summary>
    public static readonly OperatorKeyRequirement Instance = new();

    private OperatorKeyRequirement()
    {
    }
}

/// <summary>Refuses a request that does not carry the configured operator key.</summary>
internal sealed class OperatorKeyFilter(IOptions<OperatorKeyOptions> options) : IEndpointFilter
{
    private const string Scheme = "Bearer ";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var configured = options.Value;

        if (!configured.WritesEnabled)
        {
            return TypedResults.Problem(
                detail: "This deployment has no operator key, so it accepts no writes. "
                    + "Set PQT_OPERATOR_KEY and restart the API to enable them.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Writes are disabled.");
        }

        if (!Presented(context.HttpContext.Request, configured.OperatorKey))
        {
            // Missing and wrong get the same answer: telling them apart would
            // tell a caller probing the endpoint which half it had right.
            context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";

            return TypedResults.Problem(
                detail: "This request needs the operator key, sent as 'Authorization: Bearer <key>'.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Operator key required.");
        }

        return await next(context).ConfigureAwait(false);
    }

    private static bool Presented(HttpRequest request, string expected)
    {
        var header = request.Headers[HeaderNames.Authorization].ToString();

        if (!header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Hashed first so the comparison takes the same time whatever the
        // presented key's length, then compared in constant time.
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[Scheme.Length..]));
        var wanted = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(presented, wanted);
    }
}

/// <summary>Attaches the operator-key guard to a route group.</summary>
internal static class OperatorKeyEndpointExtensions
{
    /// <summary>Requires the operator key on every endpoint in the group.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The group or endpoint.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static TBuilder RequireOperatorKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter<TBuilder, OperatorKeyFilter>();
        builder.WithMetadata(OperatorKeyRequirement.Instance);

        return builder;
    }
}
