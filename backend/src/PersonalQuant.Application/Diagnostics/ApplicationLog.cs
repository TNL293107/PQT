using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Instruments;

namespace PersonalQuant.Application.Diagnostics;

/// <summary>
/// Source-generated log messages for the application layer.
/// </summary>
/// <remarks>
/// <para>
/// Compile-time generated delegates, for the reasons given on the
/// infrastructure equivalent: instrument search runs on every keystroke of the
/// command bar, and a log call on that path should not be parsing a format
/// string.
/// </para>
/// <para>
/// Query text is logged at debug only. It is not sensitive — a ticker or a
/// company name — but it is user input, and there is no reason for it to sit
/// in production logs when a length and a result count answer every question
/// worth asking about search behaviour.
/// </para>
/// </remarks>
internal static partial class ApplicationLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "instrument.search matched {ResultCount} instrument(s) for a {QueryLength}-character query in {ElapsedMs}ms.")]
    public static partial void InstrumentSearchCompleted(
        ILogger logger,
        int resultCount,
        int queryLength,
        long elapsedMs);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "instrument.resolve for {Symbol} ended as {Outcome} with {CandidateCount} candidate(s).")]
    public static partial void InstrumentResolved(
        ILogger logger,
        string symbol,
        InstrumentResolutionOutcome outcome,
        int candidateCount);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "instrument.resolve found {CandidateCount} active instruments for {Symbol}. The caller must disambiguate by exchange.")]
    public static partial void InstrumentSymbolAmbiguous(
        ILogger logger,
        string symbol,
        int candidateCount);
}
