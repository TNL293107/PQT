using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

/// <summary>Refuses a request to a guarded endpoint that does not carry the operator key.</summary>
/// <remarks>
/// Middleware rather than an endpoint filter, because filters run after the
/// request body is bound: as a filter, an anonymous caller could make the
/// server read and parse a large body, and learn a route's shape from its 400
/// or 415, before being told it needs a key. Here the check runs once routing
/// has chosen the endpoint and before any of it executes (ADR-027).
/// </remarks>
internal sealed class OperatorKeyMiddleware(RequestDelegate next, IOptions<OperatorKeyOptions> options)
{
    private const string Scheme = "Bearer ";

    /// <summary>Checks the key for a guarded endpoint, and passes everything else through.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the request has been answered.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint()?.Metadata.GetMetadata<OperatorKeyRequirement>() is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var configured = options.Value;

        if (!configured.WritesEnabled)
        {
            await TypedResults.Problem(
                    detail: "This deployment has no operator key, so it accepts no writes. "
                        + "Set PQT_OPERATOR_KEY and restart the API to enable them.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Writes are disabled.")
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        if (!Presented(context.Request, configured.OperatorKey))
        {
            // Missing and wrong get the same answer: telling them apart would
            // tell a caller probing the endpoint which half it had right.
            context.Response.Headers.WWWAuthenticate = "Bearer";

            await TypedResults.Problem(
                    detail: "This request needs the operator key, sent as 'Authorization: Bearer <key>'.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Operator key required.")
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        // The bound is applied here rather than left to endpoint metadata, so
        // it holds on every server the API runs on: a declared length over it
        // is refused unread, and an undeclared one is cut off where the
        // server supports a per-request limit.
        if (context.Request.ContentLength > OperatorKeyEndpointExtensions.MaxWriteBodyBytes)
        {
            await TypedResults.Problem(
                    detail: $"A write body may be at most {OperatorKeyEndpointExtensions.MaxWriteBodyBytes / 1024} KB.",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "The request body is too large.")
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = OperatorKeyEndpointExtensions.MaxWriteBodyBytes;
        }

        await next(context).ConfigureAwait(false);
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

/// <summary>Attaches and enforces the operator-key guard.</summary>
internal static class OperatorKeyEndpointExtensions
{
    /// <summary>The largest request body a write endpoint reads.</summary>
    /// <remarks>
    /// A watchlist name or fifty console lines is a few kilobytes; the
    /// default of about 28 MB would let one request make the server buffer
    /// far more than any write here needs.
    /// </remarks>
    public const long MaxWriteBodyBytes = 64 * 1024;

    /// <summary>Requires the operator key on every endpoint in the group, and bounds its body.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The group or endpoint.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static TBuilder RequireOperatorKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(OperatorKeyRequirement.Instance);

        return builder;
    }

    /// <summary>Enforces the key on every endpoint that requires it.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, to allow chaining.</returns>
    /// <remarks>Registered after CORS, so a browser's preflight is answered before the key is asked for.</remarks>
    public static IApplicationBuilder UseOperatorKey(this IApplicationBuilder app) =>
        app.UseMiddleware<OperatorKeyMiddleware>();
}
