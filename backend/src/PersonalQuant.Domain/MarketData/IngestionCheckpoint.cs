using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// How far one source has been ingested for one instrument and resolution.
/// </summary>
/// <remarks>
/// <para>
/// What makes ingestion incremental and resumable. Without it every run either
/// re-fetches the whole history — which providers rate-limit and eventually
/// refuse — or starts from a date somebody typed, which quietly leaves a hole
/// the first time a run fails overnight.
/// </para>
/// <para>
/// It records the last bar actually stored, not the end of the range that was
/// requested. A request for a week that returned three days must resume on the
/// fourth; resuming from the requested end would skip the rest of the week
/// forever, and nothing downstream would report a gap because the checkpoint
/// would claim the data was already there.
/// </para>
/// <para>
/// Keyed per source as well as per instrument and interval. Two providers
/// ingest at different speeds and fail at different times, and one checkpoint
/// shared between them would let the slower one's progress be reported as the
/// faster one's.
/// </para>
/// </remarks>
public sealed class IngestionCheckpoint
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private IngestionCheckpoint() => Source = null!;

    private IngestionCheckpoint(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode source,
        DateTimeOffset lastBarOpenedAtUtc,
        DateTimeOffset succeededAtUtc)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        Source = source;
        LastBarOpenedAtUtc = lastBarOpenedAtUtc;
        LastSucceededAtUtc = succeededAtUtc;
    }

    /// <summary>Gets the instrument the checkpoint tracks.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the checkpoint tracks.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>Gets the source the checkpoint tracks.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the opening instant of the newest bar stored so far.</summary>
    public DateTimeOffset LastBarOpenedAtUtc { get; private set; }

    /// <summary>Gets the instant the last successful run finished.</summary>
    public DateTimeOffset LastSucceededAtUtc { get; private set; }

    /// <summary>Gets the instant the next run should resume from.</summary>
    /// <remarks>
    /// One interval past the last stored bar, so a resumed run neither repeats
    /// the period it already has nor skips the one after it.
    /// </remarks>
    public DateTimeOffset ResumeFromUtc => LastBarOpenedAtUtc + Interval.ToDuration();

    /// <summary>
    /// Starts tracking an instrument, interval and source.
    /// </summary>
    /// <param name="instrumentId">The instrument ingested.</param>
    /// <param name="interval">The resolution ingested.</param>
    /// <param name="source">The provider ingested from.</param>
    /// <param name="lastBarOpenedAtUtc">The opening instant of the newest bar stored.</param>
    /// <param name="succeededAtUtc">The instant the run finished.</param>
    /// <returns>The new checkpoint.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static IngestionCheckpoint Start(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode source,
        DateTimeOffset lastBarOpenedAtUtc,
        DateTimeOffset succeededAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A checkpoint must belong to an instrument.");
        }

        if (!interval.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{interval}' is not a bar resolution this system records.");
        }

        if (!interval.IsAligned(lastBarOpenedAtUtc))
        {
            throw new DomainValidationException(
                $"A {interval} checkpoint cannot sit at {lastBarOpenedAtUtc:O}; it is not on a period boundary in UTC.");
        }

        return new IngestionCheckpoint(
            instrumentId, interval, source, lastBarOpenedAtUtc, succeededAtUtc);
    }

    /// <summary>
    /// Moves the checkpoint forward after a successful run.
    /// </summary>
    /// <remarks>
    /// Never backwards. A run that returned only older data — a provider
    /// serving a stale cache, a range requested by mistake — must not be able
    /// to make the system re-ingest history it already has, or worse, report
    /// progress it has lost. Such a run advances only the success timestamp.
    /// </remarks>
    /// <param name="lastBarOpenedAtUtc">The opening instant of the newest bar stored.</param>
    /// <param name="succeededAtUtc">The instant the run finished.</param>
    /// <returns><see langword="true"/> when the position moved.</returns>
    /// <exception cref="DomainValidationException">The instant is not on a period boundary.</exception>
    public bool Advance(DateTimeOffset lastBarOpenedAtUtc, DateTimeOffset succeededAtUtc)
    {
        if (!Interval.IsAligned(lastBarOpenedAtUtc))
        {
            throw new DomainValidationException(
                $"A {Interval} checkpoint cannot sit at {lastBarOpenedAtUtc:O}; it is not on a period boundary in UTC.");
        }

        LastSucceededAtUtc = succeededAtUtc;

        if (lastBarOpenedAtUtc <= LastBarOpenedAtUtc)
        {
            return false;
        }

        LastBarOpenedAtUtc = lastBarOpenedAtUtc;
        return true;
    }

    /// <summary>
    /// Records that a run succeeded without returning anything newer.
    /// </summary>
    /// <remarks>
    /// The ordinary outcome of polling a source between sessions, and worth
    /// distinguishing from a run that failed: "nothing new" and "we could not
    /// tell" look identical from the outside and mean opposite things.
    /// </remarks>
    /// <param name="succeededAtUtc">The instant the run finished.</param>
    public void RecordSuccessWithoutProgress(DateTimeOffset succeededAtUtc) =>
        LastSucceededAtUtc = succeededAtUtc;
}
