using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>Reads and records the venues instruments traded on over time.</summary>
public interface IVenueHistoryRepository
{
    /// <summary>Reads every recorded spell of the instruments named.</summary>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The spells, in no particular order; none for an instrument that never moved.</returns>
    Task<IReadOnlyList<InstrumentVenueSpell>> ListAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new spell.</summary>
    /// <param name="spell">The spell.</param>
    void Add(InstrumentVenueSpell spell);
}

/// <summary>Builds venue timelines for a set of instruments.</summary>
public static class VenueTimelines
{
    /// <summary>
    /// Reads the spells of a set of instruments in one query and builds each
    /// one's timeline.
    /// </summary>
    /// <param name="history">The venue history.</param>
    /// <param name="currentVenues">Each instrument and the venue it is on now.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A timeline for every instrument named.</returns>
    public static async Task<IReadOnlyDictionary<InstrumentId, VenueTimeline>> LoadAsync(
        IVenueHistoryRepository history,
        IReadOnlyDictionary<InstrumentId, Domain.Exchanges.ExchangeId> currentVenues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(currentVenues);

        var spells = currentVenues.Count == 0
            ? []
            : await history.ListAsync([.. currentVenues.Keys], cancellationToken).ConfigureAwait(false);

        var byInstrument = spells.ToLookup(spell => spell.InstrumentId);

        return currentVenues.ToDictionary(
            pair => pair.Key,
            pair => new VenueTimeline(pair.Value, byInstrument[pair.Key]));
    }
}

/// <summary>A source of recorded venue changes.</summary>
public interface IVenueHistoryProvider
{
    /// <summary>Gets the code spells from this source are attributed to.</summary>
    SourceCode Code { get; }

    /// <summary>Reads every spell the source states.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The spells.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">The source could not be read.</exception>
    Task<IReadOnlyList<ProviderVenueSpell>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>One venue spell exactly as a source stated it.</summary>
/// <param name="Symbol">The source's spelling of the symbol, such as <c>BSR.HM</c>.</param>
/// <param name="Venue">The venue's code, such as <c>UPCOM</c>.</param>
/// <param name="EffectiveFrom">The first session there, or null for "since before any record".</param>
/// <param name="EffectiveTo">The first session elsewhere, or null while it trades there.</param>
/// <param name="LineNumber">Where the row came from, for a rejection to point at.</param>
public sealed record ProviderVenueSpell(
    string Symbol,
    string Venue,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    int LineNumber);

/// <summary>Why an instrument's stated venue history was not recorded.</summary>
public enum VenueHistoryRejectionReason
{
    /// <summary>The symbol names no instrument this system holds.</summary>
    UnknownInstrument = 1,

    /// <summary>A venue code is not registered.</summary>
    UnknownVenue = 2,

    /// <summary>
    /// The spells do not tile time: a gap, an overlap, a first spell with a
    /// start, or a last one with an end.
    /// </summary>
    NotContiguous = 3,

    /// <summary>The last spell's venue is not the one the master has the instrument on now.</summary>
    DisagreesWithCurrentVenue = 4,

    /// <summary>A history is already recorded and this one differs from it.</summary>
    ContradictsRecordedHistory = 5,
}

/// <summary>One instrument whose stated history was refused, and why.</summary>
/// <param name="Symbol">The symbol as the source spelled it.</param>
/// <param name="Reason">Why.</param>
/// <param name="Detail">A sentence for the operator.</param>
public sealed record VenueHistoryRejection(string Symbol, VenueHistoryRejectionReason Reason, string Detail);

/// <summary>What a venue history import did.</summary>
/// <param name="Source">The source read.</param>
/// <param name="RowsRead">How many spells the source stated.</param>
/// <param name="InstrumentsRecorded">Instruments whose history was recorded now.</param>
/// <param name="InstrumentsUnchanged">Instruments whose history was already recorded as stated.</param>
/// <param name="FindingsRejudged">
/// Open price-limit breaches explained because the session, judged on the venue
/// it actually traded on, moved within that venue's band.
/// </param>
/// <param name="Rejections">Instruments refused, with the reason.</param>
public sealed record VenueHistoryImportReport(
    string Source,
    int RowsRead,
    int InstrumentsRecorded,
    int InstrumentsUnchanged,
    int FindingsRejudged,
    IReadOnlyList<VenueHistoryRejection> Rejections);

/// <summary>Records instruments' venue history from a source.</summary>
public interface IVenueHistoryImportService
{
    /// <summary>Reads the registered source and records what it states.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What was recorded and what was refused.</returns>
    Task<VenueHistoryImportReport> ImportAsync(CancellationToken cancellationToken = default);
}
