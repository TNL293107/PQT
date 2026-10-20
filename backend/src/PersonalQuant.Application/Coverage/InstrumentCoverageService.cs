using System.Globalization;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Coverage;

/// <summary>
/// Default <see cref="IInstrumentCoverageService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The state decisions live here and nowhere else. A surface that decided for
/// itself whether a universe's silence meant "not a member" would be the one
/// that got it wrong, and ADR-026 forbids any surface rendering
/// <see cref="CoverageState.NotMeasured"/> as <see cref="CoverageState.NotHeld"/>.
/// </para>
/// <para>
/// A fixed number of round trips whatever the instrument's history: one
/// grouped read each for bars, findings, attempts, actions and spells, plus the
/// calendar window for the daily series. Nothing is read per resolution.
/// </para>
/// </remarks>
/// <param name="instruments">Resolves the instrument and its venue.</param>
/// <param name="exchanges">Names the venue.</param>
/// <param name="calendar">The venue's trading days, and whether they are known.</param>
/// <param name="coverage">The aggregate reads.</param>
/// <param name="universes">Every universe PQT defines, with its coverage claim.</param>
/// <param name="providers">Which resolutions any registered source collects.</param>
internal sealed class InstrumentCoverageService(
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    ITradingCalendar calendar,
    ICoverageRepository coverage,
    IUniverseRepository universes,
    IMarketDataProviderRegistry providers,
    Fundamentals.IFundamentalsRepository fundamentals) : IInstrumentCoverageService
{
    /// <summary>
    /// The resolutions reported, daily first and then from coarse to fine.
    /// </summary>
    /// <remarks>
    /// Every declared resolution, not only the ones held. A resolution PQT
    /// collects and holds nothing of is an answer; leaving it out would make
    /// it indistinguishable from one PQT has never heard of.
    /// </remarks>
    internal static readonly IReadOnlyList<BarInterval> Resolutions =
    [
        BarInterval.OneDay,
        BarInterval.OneHour,
        BarInterval.ThirtyMinutes,
        BarInterval.FifteenMinutes,
        BarInterval.FiveMinutes,
        BarInterval.OneMinute,
    ];

    /// <summary>
    /// Names the first fundamentals import leaves out, and why (ADR-029).
    /// </summary>
    /// <remarks>
    /// Their coverage is <see cref="CoverageState.NotCollected"/> with the
    /// reason, never <see cref="CoverageState.NotHeld"/>: PQT did not look,
    /// or could not, which is a different fact from having looked and found
    /// nothing.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> FundamentalsNotCollected =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SSI"] = "securities firm, outside the first import (ADR-029)",
            ["TCX"] = "securities firm, outside the first import (ADR-029)",
            ["BVH"] = "insurer, outside the first import (ADR-029)",
            ["STB"] = "IDS fails on every filing page (ADR-029, known gap)",
            ["GVR"] = "IDS fails on every filing page (ADR-029, known gap)",
        };

    /// <summary>
    /// The same names, as the code a dataset's <c>not_collected</c> carries
    /// (ADR-031): a capture gap may be fixed, a name out of scope will not be.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> FundamentalsGapCodes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SSI"] = "out_of_scope",
            ["TCX"] = "out_of_scope",
            ["BVH"] = "out_of_scope",
            ["STB"] = "ids_capture_gap",
            ["GVR"] = "ids_capture_gap",
        };

    /// <inheritdoc />
    public async Task<InstrumentCoverage?> DescribeAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        if (instrumentId.IsEmpty)
        {
            return null;
        }

        var instrument = await instruments
            .FindByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return null;
        }

        // The foreign key guarantees the venue; a missing one is corruption,
        // not an answer, and is not papered over with a blank code.
        var venue = await exchanges
            .FindByIdAsync(instrument.ExchangeId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Instrument {instrumentId} names venue {instrument.ExchangeId}, which does not exist.");

        var prices = await DescribePricesAsync(instrument, venue.Code, cancellationToken)
            .ConfigureAwait(false);

        var actions = await coverage
            .SummariseCorporateActionsAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        var memberships = await DescribeUniversesAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        return new InstrumentCoverage(
            instrumentId,
            instrument.Ticker,
            instrument.Name,
            venue.Code,
            prices,
            DescribeCorporateActions(actions),
            memberships,
            DescribeFundamentals(
                instrument.Ticker,
                await fundamentals.SummariseAsync(instrumentId, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Decides the state of the fundamentals dataset (sprint 7.2).
    /// </summary>
    /// <remarks>
    /// Reports and periods are counted, not rows: a report is a disclosure,
    /// and a report disclosed with an empty form is counted apart, because it
    /// is a period PQT knows was published and holds no values for.
    /// </remarks>
    private static FundamentalsCoverage DescribeFundamentals(Ticker ticker, Fundamentals.FundamentalsSummary summary)
    {
        if (summary.Reports > 0)
        {
            return new FundamentalsCoverage(
                CoverageState.Held,
                summary.Reports,
                summary.WithoutValues,
                summary.FirstPeriod,
                summary.LastPeriod,
                summary.LastDisclosedOn,
                summary.Sources,
                summary.WithoutValues > 0
                    ? $"{summary.WithoutValues.ToString(CultureInfo.InvariantCulture)} disclosed without values"
                    : null);
        }

        return FundamentalsNotCollected.TryGetValue(ticker.Value, out var reason)
            ? new FundamentalsCoverage(CoverageState.NotCollected, 0, 0, null, null, null, [], reason)
            : new FundamentalsCoverage(CoverageState.NotHeld, 0, 0, null, null, null, [], "no report recorded");
    }

    /// <summary>
    /// Decides the state of the corporate action dataset.
    /// </summary>
    /// <remarks>
    /// PQT collects corporate actions for every instrument, so none recorded is
    /// <see cref="CoverageState.NotHeld"/> — literally true of what is held,
    /// and stated as that rather than as "the issuer did nothing".
    /// </remarks>
    /// <param name="extent">What is recorded.</param>
    /// <returns>The section.</returns>
    internal static CorporateActionCoverage DescribeCorporateActions(CorporateActionExtent extent) =>
        extent.Count > 0
            ? new CorporateActionCoverage(
                CoverageState.Held,
                extent.Count,
                extent.Cancelled,
                extent.WithoutAnnouncementDate,
                extent.FirstExDate,
                extent.LastExDate,
                Note: null)
            : new CorporateActionCoverage(
                CoverageState.NotHeld,
                0,
                0,
                0,
                null,
                null,
                "No corporate action is recorded for this instrument.");

    /// <summary>
    /// Decides the state of one universe's membership.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spells make it <see cref="CoverageState.Held"/>, whatever the claim:
    /// a recorded spell is a fact even where the universe claims nothing.
    /// </para>
    /// <para>
    /// Without spells, the claim decides. A universe that declared a window
    /// knows the instrument was not a member inside it, and that is
    /// <see cref="CoverageState.NotHeld"/> for the window only. A universe that
    /// declared nothing knows nothing, and that is
    /// <see cref="CoverageState.NotMeasured"/> — never "not a member".
    /// </para>
    /// </remarks>
    /// <param name="universe">The universe.</param>
    /// <param name="spells">The instrument's spells in it.</param>
    /// <returns>The section.</returns>
    internal static UniverseMembershipCoverage DescribeMembership(
        Universe universe,
        IReadOnlyList<UniverseSpell> spells)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(spells);

        var claim = universe.Coverage;
        var code = universe.Code.Value;

        var (state, note) = (spells.Count > 0, claim) switch
        {
            (true, { } known) => (
                CoverageState.Held,
                $"{code} claims to know its membership {Window(known)}; outside that, a date "
                    + "with no spell is unknown rather than non-membership."),
            (true, null) => (
                CoverageState.Held,
                $"{code} declares no coverage: these are the spells recorded, and a date "
                    + "outside them is unknown rather than non-membership."),
            (false, { } known) => (
                CoverageState.NotHeld,
                $"Not a member at any time {Window(known)}, the window {code} declares; "
                    + "outside it, unknown."),
            (false, null) => (
                CoverageState.NotMeasured,
                $"{code} declares no coverage, so the absence of a spell says nothing "
                    + "about membership."),
        };

        return new UniverseMembershipCoverage(
            universe.Code,
            universe.Name,
            state,
            claim?.From,
            claim?.Until,
            spells,
            note);
    }

    private async Task<IReadOnlyList<PriceCoverage>> DescribePricesAsync(
        Instrument instrument,
        ExchangeCode venue,
        CancellationToken cancellationToken)
    {
        var extents = await coverage
            .SummariseBarsAsync(instrument.Id, cancellationToken)
            .ConfigureAwait(false);

        var findings = await coverage
            .CountOpenFindingsAsync(instrument.Id, cancellationToken)
            .ConfigureAwait(false);

        var attempts = (await coverage
            .ListLatestAttemptsAsync(instrument.Id, cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(attempt => attempt.Interval);

        var described = new List<PriceCoverage>(Resolutions.Count);

        foreach (var interval in Resolutions)
        {
            var held = extents.Where(extent => extent.Interval == interval).ToList();
            var openFindings = findings.GetValueOrDefault(interval);
            var lastAttempt = attempts.GetValueOrDefault(interval);

            described.Add(held.Count > 0
                ? await DescribeHeldAsync(instrument, venue, interval, held, openFindings, lastAttempt, cancellationToken)
                    .ConfigureAwait(false)
                : DescribeAbsent(instrument, venue, interval, openFindings, lastAttempt));
        }

        return described;
    }

    private async Task<PriceCoverage> DescribeHeldAsync(
        Instrument instrument,
        ExchangeCode venue,
        BarInterval interval,
        IReadOnlyList<BarSeriesExtent> held,
        int openFindings,
        IngestionAttempt? lastAttempt,
        CancellationToken cancellationToken)
    {
        var first = Session(held.Min(extent => extent.FirstOpenedAtUtc));
        var last = Session(held.Max(extent => extent.LastOpenedAtUtc));

        var sources = held
            .Select(extent => extent.Source)
            .DistinctBy(source => source.Value)
            .OrderBy(source => source.Value, StringComparer.Ordinal)
            .ToList();

        var (expected, missing, note) = interval == BarInterval.OneDay
            ? await CountMissingSessionsAsync(instrument, venue, first, last, cancellationToken)
                .ConfigureAwait(false)
            : (null, null, "Missing sessions are measured for the daily series only.");

        return new PriceCoverage(
            interval,
            CoverageState.Held,
            first,
            last,
            held.Sum(extent => extent.Bars),
            sources,
            openFindings,
            lastAttempt,
            expected,
            missing,
            note);
    }

    /// <summary>
    /// Counts the sessions between the first and last bar that have no bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expected sessions are the venue calendar's trading days — the same
    /// <see cref="TradingCalendarWindow.TradingDays"/> the quality score's
    /// completeness counts — and only when the calendar covers the whole span.
    /// A span reaching past the transcribed calendar cannot tell a holiday from
    /// a gap, and the count is then absent rather than zero.
    /// </para>
    /// <para>
    /// Bounded by the series' own ends, not by today. A series that stopped
    /// being ingested is stale, and that is what its last session and its last
    /// attempt say; counting every session since as missing would describe the
    /// schedule, not the history.
    /// </para>
    /// </remarks>
    private async Task<(int? Expected, int? Missing, string? Note)> CountMissingSessionsAsync(
        Instrument instrument,
        ExchangeCode venue,
        DateOnly first,
        DateOnly last,
        CancellationToken cancellationToken)
    {
        var window = await calendar
            .LoadAsync(instrument.ExchangeId, first, last, cancellationToken)
            .ConfigureAwait(false);

        if (!window.IsComplete)
        {
            return (null, null,
                $"Missing sessions not measured: the {venue.Value} calendar is not declared for "
                    + $"the whole of {Day(first)} to {Day(last)}.");
        }

        var sessions = window.TradingDays().ToList();

        var present = await coverage
            .CountDailyBarsOnAsync(instrument.Id, sessions, cancellationToken)
            .ConfigureAwait(false);

        return (sessions.Count, sessions.Count - present, null);
    }

    private PriceCoverage DescribeAbsent(
        Instrument instrument,
        ExchangeCode venue,
        BarInterval interval,
        int openFindings,
        IngestionAttempt? lastAttempt)
    {
        var resolution = BarIntervalParser.Describe(interval);

        // Collected means a registered source could serve this resolution for
        // this venue and asset type. Nothing held where nothing could be
        // fetched is a gap in what PQT collects, not in this instrument's data.
        var collected = providers.Providers.Any(provider =>
            provider.Capability.Serves(interval)
            && provider.Capability.Covers(venue)
            && provider.Capability.Serves(instrument.AssetType));

        return new PriceCoverage(
            interval,
            collected ? CoverageState.NotHeld : CoverageState.NotCollected,
            null,
            null,
            0,
            [],
            openFindings,
            lastAttempt,
            null,
            null,
            collected
                ? $"No {resolution} bars are stored."
                : $"No registered source serves {resolution} bars for {venue.Value} "
                    + $"{instrument.AssetType}.");
    }

    private async Task<IReadOnlyList<UniverseMembershipCoverage>> DescribeUniversesAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        var defined = await universes.ListAsync(cancellationToken).ConfigureAwait(false);

        var spells = (await coverage
            .ListUniverseSpellsAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false))
            .ToLookup(entry => entry.UniverseId, entry => entry.Spell);

        return
        [
            .. defined
                .OrderBy(universe => universe.Code.Value, StringComparer.Ordinal)
                .Select(universe => DescribeMembership(
                    universe,
                    [.. spells[universe.Id].OrderBy(spell => spell.EffectiveFrom)])),
        ];
    }

    /// <summary>
    /// Writes a claim as the dates it covers, inclusive at both ends.
    /// </summary>
    /// <remarks>
    /// The claim is stored half-open; a reader is told the last date it covers
    /// rather than the first it does not, which is the off-by-one a reader would
    /// otherwise make.
    /// </remarks>
    private static string Window(MembershipCoverage claim) =>
        claim.Until is { } until
            ? $"from {Day(claim.From)} through {Day(until.AddDays(-1))}"
            : $"from {Day(claim.From)} onward";

    private static DateOnly Session(DateTimeOffset openedAtUtc) =>
        DateOnly.FromDateTime(openedAtUtc.UtcDateTime);

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
