using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Excludes a second ingestion pass from a series while one is running.
/// </summary>
/// <remarks>
/// <para>
/// A series is one instrument at one resolution. Two passes over the same
/// series — the scheduled host and an operator's <c>pqt ingest</c>, or two
/// operators — read the same checkpoint, request the same range and upsert the
/// same bars. Whichever commits second either fails on the bar key or, worse,
/// succeeds and moves the checkpoint backwards over bars the first pass stored.
/// Neither process can see the other, so the exclusion has to live outside both.
/// </para>
/// <para>
/// The key is the series and never the source. The rule that a raw source and a
/// source-adjusted one may not share a series is checked by reading which
/// sources the series already holds; two passes from different sources
/// checking it concurrently would each find the other absent and both write.
/// </para>
/// <para>
/// A port, so the application layer names what it needs and not where it is
/// kept. The implementation decides the store, and a test substitutes one.
/// </para>
/// </remarks>
public interface ISeriesLock
{
    /// <summary>
    /// Takes the series for the caller, if nobody else holds it.
    /// </summary>
    /// <remarks>
    /// Never waits for the holder. A pass that queued behind another would
    /// re-fetch whatever range the first one leaves behind it, and the next
    /// scheduled or operator pass does that anyway, with a fresh checkpoint.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>
    /// The lease, to be disposed when the pass ends, or <see langword="null"/>
    /// when another pass holds the series.
    /// </returns>
    /// <exception cref="SeriesLockUnavailableException">
    /// The lock store could not be reached, so whether the series is free is
    /// not known.
    /// </exception>
    Task<ISeriesLease?> TryAcquireAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A held series. Disposing it releases the series.
/// </summary>
/// <remarks>
/// Release takes no cancellation token on purpose. It runs while a cancelled
/// pass unwinds, and a release abandoned because the caller was cancelled would
/// keep every other pass off the series until the lease expired on its own.
/// </remarks>
public interface ISeriesLease : IAsyncDisposable;

/// <summary>
/// The lock store could not answer, so a series could not be taken.
/// </summary>
/// <remarks>
/// Thrown rather than read as "free". Not knowing whether another pass holds a
/// series is not the same as knowing nobody does, and proceeding on the first
/// as though it were the second is exactly the race the lock exists to remove.
/// The message is recorded on the run and must name the store without
/// disclosing where it is.
/// </remarks>
public sealed class SeriesLockUnavailableException : Exception
{
    /// <summary>Creates an exception with the default message.</summary>
    public SeriesLockUnavailableException()
        : this("The ingestion lock store could not be reached.")
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What could not be reached, safe to record.</param>
    public SeriesLockUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception wrapping the underlying failure.</summary>
    /// <param name="message">What could not be reached, safe to record.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SeriesLockUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The reasons a run records when the series lock decided its outcome.
/// </summary>
/// <remarks>
/// Public and constant so that a caller looping over passes can tell "another
/// pass has the series" from "the range is exhausted". Both are skips that
/// fetched nothing, and a backfill that read the first as the second would
/// report itself complete while the other pass was still writing.
/// </remarks>
public static class SeriesLockReasons
{
    /// <summary>
    /// Recorded on a skipped run when another pass held the series.
    /// </summary>
    public const string Busy =
        "Another ingestion of this series is in progress, so this pass did not run. "
        + "Re-run once it has finished; it will resume from the checkpoint the other pass leaves.";

    /// <summary>
    /// Builds the reason recorded on a run the lock store could not admit.
    /// </summary>
    /// <param name="exception">Why the store could not answer.</param>
    /// <returns>A caller-safe reason.</returns>
    public static string Unavailable(SeriesLockUnavailableException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return $"{exception.Message} The pass did not run: ingesting without the series lock "
            + "could race another pass on the checkpoint and the bars.";
    }

    /// <summary>
    /// Reports whether a run was skipped because another pass held its series.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns><see langword="true"/> when the series was busy.</returns>
    public static bool IsBusy(IngestionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Outcome == IngestionOutcome.Skipped
            && string.Equals(run.FailureReason, Busy, StringComparison.Ordinal);
    }
}
