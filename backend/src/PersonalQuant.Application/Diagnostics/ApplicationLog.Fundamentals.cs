using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Fundamentals;

namespace PersonalQuant.Application.Diagnostics;

/// <summary>
/// Log messages for the fundamentals import (Phase 6).
/// </summary>
internal static partial class ApplicationLog
{
    [LoggerMessage(
        EventId = 3700,
        Level = LogLevel.Information,
        Message = "fundamentals from {Provider}: {Filings} filing(s) read; {Recorded} report(s) recorded, {WithoutValues} without values, {AlreadyHeld} already held; {Lines} line(s) recorded, {Restated} restated; {Late} late.")]
    public static partial void FundamentalsImported(
        ILogger logger,
        string provider,
        int filings,
        int recorded,
        int withoutValues,
        int alreadyHeld,
        int lines,
        int restated,
        int late);

    [LoggerMessage(
        EventId = 3701,
        Level = LogLevel.Warning,
        Message = "filing {Reference} ({Ticker}) was refused, {Reason}: {Detail}")]
    public static partial void FilingRejected(
        ILogger logger,
        string reference,
        string ticker,
        FilingRejectionReason reason,
        string detail);
}
