using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Breadth;

namespace PersonalQuant.Application.Diagnostics;

/// <summary>
/// Log messages for market breadth.
/// </summary>
/// <remarks>
/// A file of its own so each Phase 5 terminal function adds its messages
/// without editing one shared list. Debug only: breadth is a read the console
/// issues on demand, and a line per request at information level would bury
/// the ingestion and import lines operators actually read.
/// </remarks>
internal static partial class ApplicationLog
{
    [LoggerMessage(
        EventId = 3610,
        Level = LogLevel.Debug,
        Message = "breadth {Universe} on {Session}: {Advancing} advancing, {Declining} declining, {Unchanged} unchanged, {NoData} without data.")]
    public static partial void BreadthComputed(
        ILogger logger,
        string universe,
        string session,
        int advancing,
        int declining,
        int unchanged,
        int noData);

    [LoggerMessage(
        EventId = 3611,
        Level = LogLevel.Debug,
        Message = "breadth {Universe} was refused: {Outcome}.")]
    public static partial void BreadthRefused(
        ILogger logger,
        string universe,
        BreadthOutcome outcome);
}
