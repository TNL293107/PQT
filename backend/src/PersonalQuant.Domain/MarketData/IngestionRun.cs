using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>The identifier of an <see cref="IngestionRun"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct IngestionRunId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static IngestionRunId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>How an ingestion run ended.</summary>
public enum IngestionOutcome
{
    /// <summary>Started and not yet finished.</summary>
    Running = 1,

    /// <summary>Completed. Bars may still have been rejected.</summary>
    Succeeded = 2,

    /// <summary>The provider or the persistence step failed.</summary>
    Failed = 3,

    /// <summary>
    /// Not attempted, because there was nothing to ask for.
    /// </summary>
    Skipped = 4,
}

/// <summary>
/// The audit record of one attempt to ingest one instrument, resolution and
/// range from one source.
/// </summary>
/// <remarks>
/// <para>
/// Written whether the attempt succeeded or not, and that is the point. A
/// pipeline that only records its successes cannot answer the question anyone
/// actually asks of it — "is this series complete, and if not, why?" — and a
/// gap with no failed run beside it is indistinguishable from a day the market
/// was closed.
/// </para>
/// <para>
/// The counts are separated rather than summed. Fetched, accepted, rejected,
/// stored and revised each mean something different, and collapsing them would
/// hide the case that matters most: a run that fetched a thousand bars, was
/// handed a thousand, rejected all of them, and reported success.
/// </para>
/// </remarks>
public sealed class IngestionRun
{
    /// <summary>Longest failure reason retained.</summary>
    public const int MaxFailureReasonLength = 1000;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private IngestionRun() => Source = null!;

    private IngestionRun(
        IngestionRunId id,
        SourceCode source,
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset requestedFromUtc,
        DateTimeOffset requestedToUtc,
        DateTimeOffset startedAtUtc)
    {
        Id = id;
        Source = source;
        InstrumentId = instrumentId;
        Interval = interval;
        RequestedFromUtc = requestedFromUtc;
        RequestedToUtc = requestedToUtc;
        StartedAtUtc = startedAtUtc;
        Outcome = IngestionOutcome.Running;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public IngestionRunId Id { get; private set; }

    /// <summary>Gets the provider the run read from.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the instrument the run was for.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the run was for.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>Gets the inclusive start of the requested range.</summary>
    public DateTimeOffset RequestedFromUtc { get; private set; }

    /// <summary>Gets the exclusive end of the requested range.</summary>
    public DateTimeOffset RequestedToUtc { get; private set; }

    /// <summary>Gets the instant the run started, in UTC.</summary>
    public DateTimeOffset StartedAtUtc { get; private set; }

    /// <summary>Gets the instant the run finished, once it has.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Gets how the run ended.</summary>
    public IngestionOutcome Outcome { get; private set; }

    /// <summary>Gets how many bars the provider returned.</summary>
    public int BarsFetched { get; private set; }

    /// <summary>Gets how many of them passed validation.</summary>
    public int BarsAccepted { get; private set; }

    /// <summary>Gets how many of them were rejected.</summary>
    public int BarsRejected { get; private set; }

    /// <summary>Gets how many were stored as periods not previously held.</summary>
    public int BarsStored { get; private set; }

    /// <summary>Gets how many restated a period already held.</summary>
    public int BarsRevised { get; private set; }

    /// <summary>Gets how many attempts the provider needed.</summary>
    /// <remarks>
    /// One on a clean run. More means the retry policy engaged, which is the
    /// signal that a source is degrading before it starts failing outright.
    /// </remarks>
    public int Attempts { get; private set; }

    /// <summary>
    /// Gets the raw payload this run produced, when one was retained.
    /// </summary>
    public RawBatchId? RawBatchId { get; private set; }

    /// <summary>
    /// Gets why the run failed, or <see langword="null"/> when it did not.
    /// </summary>
    /// <remarks>
    /// A message, never an exception dump. It is read by a human deciding
    /// whether to re-run, and a stack trace in a durable audit table is both
    /// useless for that and a way to leak internals into a response.
    /// </remarks>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Opens an audit record for an attempt about to be made.
    /// </summary>
    /// <param name="source">The provider being read.</param>
    /// <param name="instrumentId">The instrument requested.</param>
    /// <param name="interval">The resolution requested.</param>
    /// <param name="requestedFromUtc">The inclusive start of the range.</param>
    /// <param name="requestedToUtc">The exclusive end of the range.</param>
    /// <param name="startedAtUtc">The instant the attempt started.</param>
    /// <returns>A run in <see cref="IngestionOutcome.Running"/>.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static IngestionRun Start(
        SourceCode source,
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset requestedFromUtc,
        DateTimeOffset requestedToUtc,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("An ingestion run must name an instrument.");
        }

        if (!interval.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{interval}' is not a bar resolution this system records.");
        }

        if (requestedToUtc <= requestedFromUtc)
        {
            throw new DomainValidationException(
                $"A requested range must end after it starts, but {requestedToUtc:O} does not follow {requestedFromUtc:O}.");
        }

        return new IngestionRun(
            IngestionRunId.New(),
            source,
            instrumentId,
            interval,
            requestedFromUtc,
            requestedToUtc,
            startedAtUtc);
    }

    /// <summary>
    /// Closes the record as successful.
    /// </summary>
    /// <param name="counts">What the run did.</param>
    /// <param name="attempts">How many provider calls it took.</param>
    /// <param name="rawBatchId">The retained payload, when there is one.</param>
    /// <param name="completedAtUtc">The instant the run finished.</param>
    /// <exception cref="DomainStateException">The run has already finished.</exception>
    public void Succeed(
        IngestionCounts counts,
        int attempts,
        RawBatchId? rawBatchId,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(counts);

        RequireRunning();

        BarsFetched = counts.Fetched;
        BarsAccepted = counts.Accepted;
        BarsRejected = counts.Rejected;
        BarsStored = counts.Stored;
        BarsRevised = counts.Revised;
        Attempts = attempts;
        RawBatchId = rawBatchId;
        Outcome = IngestionOutcome.Succeeded;
        CompletedAtUtc = completedAtUtc;
    }

    /// <summary>
    /// Closes the record as failed.
    /// </summary>
    /// <param name="reason">A short, caller-safe explanation.</param>
    /// <param name="attempts">How many provider calls were made.</param>
    /// <param name="completedAtUtc">The instant the run gave up.</param>
    /// <exception cref="DomainStateException">The run has already finished.</exception>
    public void Fail(string reason, int attempts, DateTimeOffset completedAtUtc)
    {
        RequireRunning();

        Attempts = attempts;
        FailureReason = Truncate(reason);
        Outcome = IngestionOutcome.Failed;
        CompletedAtUtc = completedAtUtc;
    }

    /// <summary>
    /// Closes the record as not attempted.
    /// </summary>
    /// <remarks>
    /// Recorded rather than silently returning, so that "we looked and there
    /// was nothing to ask for" leaves a trace. A schedule that skips every
    /// night for a month is a bug, and it is only visible if the skips are
    /// written down.
    /// </remarks>
    /// <param name="reason">Why nothing was requested.</param>
    /// <param name="completedAtUtc">The instant the decision was made.</param>
    /// <exception cref="DomainStateException">The run has already finished.</exception>
    public void Skip(string reason, DateTimeOffset completedAtUtc)
    {
        RequireRunning();

        FailureReason = Truncate(reason);
        Outcome = IngestionOutcome.Skipped;
        CompletedAtUtc = completedAtUtc;
    }

    private static string Truncate(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "No reason was recorded.";
        }

        var trimmed = reason.Trim();

        return trimmed.Length <= MaxFailureReasonLength
            ? trimmed
            : trimmed[..MaxFailureReasonLength];
    }

    private void RequireRunning()
    {
        if (Outcome != IngestionOutcome.Running)
        {
            throw new DomainStateException(
                $"Ingestion run {Id} already ended as {Outcome} and cannot be closed again.");
        }
    }
}

/// <summary>
/// What one ingestion run did, counted at each stage of the pipeline.
/// </summary>
/// <remarks>
/// Fetched is what the provider returned; accepted and rejected are what
/// validation made of it; stored and revised are what reached the database.
/// They are not expected to agree — the differences between them are the
/// diagnosis.
/// </remarks>
/// <param name="Fetched">Rows the provider returned.</param>
/// <param name="Accepted">Rows that passed validation.</param>
/// <param name="Rejected">Rows validation refused.</param>
/// <param name="Stored">Periods not previously held.</param>
/// <param name="Revised">Periods already held that the source restated.</param>
public sealed record IngestionCounts(
    int Fetched,
    int Accepted,
    int Rejected,
    int Stored,
    int Revised)
{
    /// <summary>A run that did nothing.</summary>
    public static IngestionCounts None { get; } = new(0, 0, 0, 0, 0);
}
