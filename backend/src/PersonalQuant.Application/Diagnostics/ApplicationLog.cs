using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

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

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message = "marketdata.ingest {Source}/{Ticker} {Interval} stored {Stored} bar(s), revised {Revised}, rejected {Rejected}.")]
    public static partial void MarketDataIngested(
        ILogger logger,
        string source,
        string ticker,
        BarInterval interval,
        int stored,
        int revised,
        int rejected);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Error,
        Message = "marketdata.ingest {Source}/{Ticker} {Interval} failed: {Reason}")]
    public static partial void MarketDataIngestionFailed(
        ILogger logger,
        string source,
        string ticker,
        BarInterval interval,
        string reason);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Warning,
        Message = "marketdata.ingest {Source}/{Ticker} rejected {Count} bar(s) as {Reason}. First: {Detail}")]
    public static partial void MarketDataBarsRejected(
        ILogger logger,
        string source,
        string ticker,
        BarRejectionReason reason,
        int count,
        string detail);

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Warning,
        Message = "marketdata.fetch {Source}/{Ticker} attempt {Attempt} waits {DelayMs}ms after: {Reason}")]
    public static partial void MarketDataRetryScheduled(
        ILogger logger,
        string source,
        string ticker,
        int attempt,
        long delayMs,
        string reason);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "instrument.import {Source} read {RowsRead} row(s): created {Created}, matched {Matched}, enriched {Enriched}, rejected {Rejected}.")]
    public static partial void InstrumentsImported(
        ILogger logger,
        string source,
        int rowsRead,
        int created,
        int matched,
        int enriched,
        int rejected);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Warning,
        Message = "instrument.import {Source} rejected {Count} row(s) as {Reason}. First: {Detail}")]
    public static partial void InstrumentImportRowsRejected(
        ILogger logger,
        string source,
        InstrumentImportRejectionReason reason,
        int count,
        string detail);

    [LoggerMessage(
        EventId = 3110,
        Level = LogLevel.Warning,
        Message = "dataquality {Ticker} raised {Count} {Kind} finding(s). First: {Detail}")]
    public static partial void DataQualityIssuesRaised(
        ILogger logger,
        string ticker,
        DataQualityIssueKind kind,
        int count,
        string detail);

    [LoggerMessage(
        EventId = 3120,
        Level = LogLevel.Information,
        Message = "calendar.import {Source} read {RowsRead} closure(s): created {Created}, already held {AlreadyHeld}, rejected {Rejected}.")]
    public static partial void TradingCalendarImported(
        ILogger logger,
        string source,
        int rowsRead,
        int created,
        int alreadyHeld,
        int rejected);
}
