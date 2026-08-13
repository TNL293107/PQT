using Microsoft.Extensions.Logging;

namespace PersonalQuant.Api.Diagnostics;

/// <summary>
/// Source-generated log messages for the API host.
/// </summary>
internal static partial class ApiLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path}.")]
    public static partial void UnhandledException(
        ILogger logger,
        Exception exception,
        string method,
        string path);
}
