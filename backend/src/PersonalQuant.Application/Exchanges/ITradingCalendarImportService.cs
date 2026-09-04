using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Exchanges;

/// <summary>
/// An external source of exchange trading calendars.
/// </summary>
/// <remarks>
/// Separate from the instrument and market data sources because it answers a
/// different question and usually comes from a different place — a national
/// holiday schedule is published by a government, not by a market data vendor.
/// </remarks>
public interface ITradingCalendarProvider
{
    /// <summary>Gets the code this source is known by.</summary>
    SourceCode Code { get; }

    /// <summary>
    /// Reads every closure the source knows about.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The closures.</returns>
    /// <exception cref="MarketDataProviderException">The source could not be read.</exception>
    Task<IReadOnlyList<ProviderTradingHoliday>> ListHolidaysAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how far the source claims to have been transcribed, per venue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared by the source, never derived from the closures it returned.
    /// Deriving it was the defect this method replaces: the furthest recorded
    /// closure reported a calendar transcribed to the end of 2026 as ending on
    /// 2 September, and reported 2016 — a year with no rows at all — as
    /// covered, which raised three real public holidays as missing sessions.
    /// </para>
    /// <para>
    /// A source that declares nothing is not a source that covers nothing in
    /// particular; it is a source nobody has made a claim about, and every
    /// completeness figure over it reports unmeasurable.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The claims, which may be empty.</returns>
    /// <exception cref="MarketDataProviderException">The source could not be read.</exception>
    Task<IReadOnlyList<ProviderCalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One venue's transcription claim as a source reported it.
/// </summary>
/// <param name="ExchangeCode">The venue's operating code.</param>
/// <param name="From">The first date transcribed. Inclusive.</param>
/// <param name="Until">
/// The first date not transcribed, or <see langword="null"/> when the claim
/// runs on. Exclusive.
/// </param>
public sealed record ProviderCalendarCoverage(string ExchangeCode, DateOnly From, DateOnly? Until);

/// <summary>
/// One closure as a source reported it.
/// </summary>
/// <param name="ExchangeCode">The venue's operating code.</param>
/// <param name="Date">The closed date.</param>
/// <param name="Name">What the closure is.</param>
public sealed record ProviderTradingHoliday(string ExchangeCode, DateOnly Date, string Name);

/// <summary>
/// Populates the trading calendar from an external source.
/// </summary>
/// <remarks>
/// <para>
/// Without a calendar, completeness cannot be measured: every public holiday
/// looks exactly like a failed ingestion run. Vietnam's calendar cannot be
/// derived, because Tet and the Hung Kings commemoration follow the lunar
/// calendar and the substitute days for weekend holidays are set by annual
/// decree — so it has to be imported from something that publishes it.
/// </para>
/// <para>
/// That is why nothing is seeded. A partial calendar is worse than none: with
/// the fixed-date holidays recorded and Tet absent, the system would believe
/// its calendar covers the year and report a week of real closures as missing
/// sessions.
/// </para>
/// </remarks>
public interface ITradingCalendarImportService
{
    /// <summary>
    /// Reads the configured source and records the closures it does not
    /// already hold.
    /// </summary>
    /// <remarks>
    /// Additive only. A closure already recorded is left alone, and one absent
    /// from the source is not removed — a calendar file that has been truncated
    /// must not silently reopen a market.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the run did.</returns>
    /// <exception cref="MarketDataProviderException">The source could not be read.</exception>
    /// <exception cref="InvalidOperationException">No calendar source is registered.</exception>
    Task<TradingCalendarImportReport> ImportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What one calendar import did.
/// </summary>
/// <param name="Source">The source that was read.</param>
/// <param name="RowsRead">Closures the source returned.</param>
/// <param name="Created">Closures recorded for the first time.</param>
/// <param name="AlreadyHeld">Closures already recorded.</param>
/// <param name="Rejections">Rows that could not be used, with reasons.</param>
public sealed record TradingCalendarImportReport(
    string Source,
    int RowsRead,
    int Created,
    int AlreadyHeld,
    IReadOnlyList<string> Rejections,
    int CoverageDeclared = 0);
