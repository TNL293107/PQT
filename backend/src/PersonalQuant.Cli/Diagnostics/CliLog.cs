using Microsoft.Extensions.Logging;

namespace PersonalQuant.Cli.Diagnostics;

/// <summary>
/// Source-generated log messages for the operator CLI.
/// </summary>
/// <remarks>
/// <para>
/// There is one. A command reports what it found through its own output, and a
/// log line for anything the operator is already reading would print the answer
/// twice. What is left is the failure nobody asked for.
/// </para>
/// <para>
/// <strong>At debug, not error.</strong> The stack of an unreachable database
/// is thirty frames through the connection pool, EF Core and the query
/// pipeline, and none of them say anything the one-line message does not — but
/// at error they print by default, and a command that answers an ordinary
/// connection failure with sixty lines of framework internals is one an
/// operator stops reading. The trace is kept and is one environment variable
/// away: Logging__LogLevel__Default=Debug.
/// </para>
/// </remarks>
internal static partial class CliLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "The command '{Group} {Verb}' failed.")]
    public static partial void CommandFailed(
        ILogger logger,
        Exception exception,
        string group,
        string verb);
}
