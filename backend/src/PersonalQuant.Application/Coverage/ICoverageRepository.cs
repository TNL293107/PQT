using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Coverage;

/// <summary>
/// The aggregate reads coverage introspection needs, across the tables the
/// data phases write.
/// </summary>
/// <remarks>
/// <para>
/// One port rather than a method on each aggregate's repository. Every query
/// here is shaped by the registry's question — a minimum, a maximum and a count
/// grouped by resolution — rather than by any aggregate's lifecycle, and none
/// of the writers would ever call them. Spreading them across five repositories
/// would grow five interfaces, and every fake of them, by a method only this
/// read uses.
/// </para>
/// <para>
/// Everything is aggregated in the database. Coverage is read for an
/// instrument whose daily history may be fifteen years long, and an answer that
/// materialised every bar to count them would cost in proportion to the history
/// rather than to the answer. No read here is tracked: nothing is written back.
/// </para>
/// </remarks>
public interface ICoverageRepository
{
    /// <summary>
    /// Summarises the bars held for an instrument, by resolution and source.
    /// </summary>
    /// <remarks>
    /// Grouped by source as well as resolution so the distinct sources come
    /// back in the same round trip as the extent; the caller folds the rows of
    /// one resolution together.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One row per resolution and source holding any bar.</returns>
    Task<IReadOnlyList<BarSeriesExtent>> SummariseBarsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the daily bars held on a set of sessions.
    /// </summary>
    /// <remarks>
    /// A daily bar opens at midnight UTC on its session date, so the sessions
    /// are compared as instants and the count is exact: a bar stored on a day
    /// the venue was closed is not counted, and cannot hide a session that is
    /// genuinely missing.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="sessions">The sessions to look for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many of the sessions have a daily bar.</returns>
    Task<int> CountDailyBarsOnAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<DateOnly> sessions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the open findings against an instrument, by resolution.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many open findings each resolution has; absent means none.</returns>
    Task<IReadOnlyDictionary<BarInterval, int>> CountOpenFindingsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the most recent ingestion attempt for each resolution.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>At most one attempt per resolution ever attempted.</returns>
    Task<IReadOnlyList<IngestionAttempt>> ListLatestAttemptsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarises the corporate actions recorded against an instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The summary; <see cref="CorporateActionExtent.None"/> when nothing is recorded.</returns>
    Task<CorporateActionExtent> SummariseCorporateActionsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every spell an instrument has in any universe.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The spells, ordered by universe and then by start date.</returns>
    Task<IReadOnlyList<InstrumentUniverseSpell>> ListUniverseSpellsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The bars one source holds in one resolution of a series.
/// </summary>
/// <param name="Interval">The resolution.</param>
/// <param name="Source">The source.</param>
/// <param name="Bars">How many bars it holds.</param>
/// <param name="FirstOpenedAtUtc">When the earliest of them opened.</param>
/// <param name="LastOpenedAtUtc">When the latest of them opened.</param>
public sealed record BarSeriesExtent(
    BarInterval Interval,
    SourceCode Source,
    int Bars,
    DateTimeOffset FirstOpenedAtUtc,
    DateTimeOffset LastOpenedAtUtc);

/// <summary>
/// The corporate actions recorded against an instrument, in aggregate.
/// </summary>
/// <param name="Count">How many are recorded, cancelled ones included.</param>
/// <param name="Cancelled">How many were called off.</param>
/// <param name="WithoutAnnouncementDate">How many have no announcement date.</param>
/// <param name="FirstExDate">The earliest ex-date, when any is recorded.</param>
/// <param name="LastExDate">The latest ex-date, when any is recorded.</param>
public sealed record CorporateActionExtent(
    int Count,
    int Cancelled,
    int WithoutAnnouncementDate,
    DateOnly? FirstExDate,
    DateOnly? LastExDate)
{
    /// <summary>Nothing recorded.</summary>
    public static CorporateActionExtent None { get; } = new(0, 0, 0, null, null);
}

/// <summary>
/// One spell of an instrument, with the universe it belongs to.
/// </summary>
/// <param name="UniverseId">The universe.</param>
/// <param name="Spell">The spell.</param>
public sealed record InstrumentUniverseSpell(UniverseId UniverseId, UniverseSpell Spell);
