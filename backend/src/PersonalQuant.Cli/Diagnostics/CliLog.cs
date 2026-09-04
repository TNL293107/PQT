using Microsoft.Extensions.Logging;

namespace PersonalQuant.Cli.Diagnostics;

/// <summary>
/// Source-generated log messages for the operator CLI.
/// </summary>
/// <remarks>
/// There is one. A command reports what it found through its own output, and a
/// log line for anything the operator is already reading would print the answer
/// twice. What is left is the failure nobody asked for.
/// </remarks>
internal static partial class CliLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Error,
        Message = "The command '{Group} {Verb}' failed.")]
    public static partial void CommandFailed(
        ILogger logger,
        Exception exception,
        string group,
        string verb);
}
