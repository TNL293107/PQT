using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PersonalQuant.Api.Diagnostics;

/// <summary>
/// Converts any unhandled exception into an RFC 9457 problem response.
/// </summary>
/// <remarks>
/// The response body is intentionally uniform and free of exception detail.
/// Stack traces, connection strings and provider messages stay in the log,
/// where they are useful, and never reach the client.
/// </remarks>
/// <param name="problemDetailsService">Writes the problem response.</param>
/// <param name="logger">Logger for the unhandled exception.</param>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ApiLog.UnhandledException(
            logger,
            exception,
            httpContext.Request.Method,
            httpContext.Request.Path.Value ?? string.Empty);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                Detail = "The request could not be completed. The failure has been logged.",
            },
        }).ConfigureAwait(false);
    }
}
