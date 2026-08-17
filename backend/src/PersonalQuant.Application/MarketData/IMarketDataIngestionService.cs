using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Runs the ingestion pipeline for one instrument, resolution and range.
/// </summary>
/// <remarks>
/// Fetch, validate, normalise, deduplicate, persist, audit — in that order,
/// in one transaction, with a record written whatever the outcome.
/// </remarks>
public interface IMarketDataIngestionService
{
    /// <summary>
    /// Ingests market data and returns the audit record of what happened.
    /// </summary>
    /// <remarks>
    /// Returns rather than throws for every expected failure — an unknown
    /// instrument, an unregistered source, a provider that would not answer.
    /// Each of those is a recorded run with a reason, because a scheduler
    /// needs to know that the attempt was made as much as it needs to know it
    /// failed.
    /// </remarks>
    /// <param name="instruction">What to ingest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed audit record.</returns>
    Task<IngestionRun> IngestAsync(
        IngestionInstruction instruction,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A validated instruction to ingest.
/// </summary>
/// <remarks>
/// <para>
/// The range is optional at both ends, and usually absent at both. Left open,
/// a run resumes from the checkpoint and stops at the last period that has
/// finished — which is what a schedule wants and what a human asking for "the
/// latest data" means.
/// </para>
/// <para>
/// A partly-finished period is deliberately never ingested. A daily bar
/// fetched at midday is a real number that will be a different real number by
/// the close, and storing it produces a series where the most recent bar is
/// sometimes provisional and nothing records which.
/// </para>
/// </remarks>
public sealed record IngestionInstruction
{
    private IngestionInstruction(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode? source,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        Source = source;
        FromUtc = fromUtc;
        ToUtc = toUtc;
    }

    /// <summary>Gets the instrument to ingest.</summary>
    public InstrumentId InstrumentId { get; }

    /// <summary>Gets the resolution to ingest.</summary>
    public BarInterval Interval { get; }

    /// <summary>
    /// Gets the source to read, or <see langword="null"/> to use the only one
    /// registered.
    /// </summary>
    public SourceCode? Source { get; }

    /// <summary>
    /// Gets where to start, or <see langword="null"/> to resume from the
    /// checkpoint.
    /// </summary>
    public DateTimeOffset? FromUtc { get; }

    /// <summary>
    /// Gets where to stop, or <see langword="null"/> to stop at the last
    /// finished period.
    /// </summary>
    public DateTimeOffset? ToUtc { get; }

    /// <summary>
    /// Validates an instruction.
    /// </summary>
    /// <param name="instrumentId">The instrument to ingest.</param>
    /// <param name="interval">The resolution to ingest.</param>
    /// <param name="source">The source, or null for the only registered one.</param>
    /// <param name="fromUtc">Where to start, or null to resume.</param>
    /// <param name="toUtc">Where to stop, or null for the last finished period.</param>
    /// <param name="instruction">The validated instruction when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the instruction is usable.</returns>
    public static bool TryCreate(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode? source,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        [NotNullWhen(true)] out IngestionInstruction? instruction,
        [NotNullWhen(false)] out string? problem)
    {
        instruction = null;

        if (instrumentId.IsEmpty)
        {
            problem = "An instrument is required.";
            return false;
        }

        if (!interval.IsDeclared())
        {
            problem = "The bar resolution is not one this system records.";
            return false;
        }

        var normalisedFrom = fromUtc?.ToUniversalTime();
        var normalisedTo = toUtc?.ToUniversalTime();

        if (normalisedFrom is { } start && normalisedTo is { } end && end <= start)
        {
            problem = "The range must end after it starts.";
            return false;
        }

        instruction = new IngestionInstruction(
            instrumentId, interval, source, normalisedFrom, normalisedTo);
        problem = null;
        return true;
    }
}
