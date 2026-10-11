using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Coverage;

/// <summary>
/// What PQT holds for one instrument, dataset by dataset, and to what extent.
/// </summary>
/// <remarks>
/// <para>
/// Introspection over facts the data phases already record. It owns no state
/// and is computed on every read, so it cannot drift from the tables it
/// describes — a registry that stored its own answer would be one more thing to
/// keep consistent, and the first thing to go stale.
/// </para>
/// <para>
/// Every section carries a <see cref="CoverageState"/> and a
/// <c>Note</c> written here rather than by the surfaces. The API, the CLI and
/// the console all explain an absence the same way because they all read the
/// same sentence; a fourth surface would otherwise be the one that explained it
/// differently.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The instrument described.</param>
/// <param name="Ticker">Its current ticker, for display.</param>
/// <param name="Name">Its name, for display.</param>
/// <param name="Exchange">The venue it trades on.</param>
/// <param name="Prices">One entry per resolution the system records, daily first.</param>
/// <param name="CorporateActions">The corporate actions recorded against it.</param>
/// <param name="Universes">Every universe PQT defines, ordered by code.</param>
/// <param name="Fundamentals">Fundamentals, which PQT does not collect yet.</param>
public sealed record InstrumentCoverage(
    InstrumentId InstrumentId,
    Ticker Ticker,
    string Name,
    ExchangeCode Exchange,
    IReadOnlyList<PriceCoverage> Prices,
    CorporateActionCoverage CorporateActions,
    IReadOnlyList<UniverseMembershipCoverage> Universes,
    DatasetCoverage Fundamentals);

/// <summary>
/// One resolution of the price series.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MissingSessions"/> is null whenever it was not measured, and
/// never zero in its place. Zero is a claim that every session the venue traded
/// has a bar; a span the calendar was never transcribed for cannot support that
/// claim, and neither can an intraday series, whose completeness is a count of
/// periods rather than of sessions.
/// </para>
/// <para>
/// The last ingestion attempt is reported whatever its outcome, and whether or
/// not any bars are held. A resolution with no bars and a failed attempt beside
/// it is a different situation from one nobody has asked for.
/// </para>
/// </remarks>
/// <param name="Interval">The resolution.</param>
/// <param name="State">Held, not held, or not collected.</param>
/// <param name="FirstSession">The first session held, when any is.</param>
/// <param name="LastSession">The last session held, when any is.</param>
/// <param name="Bars">How many bars are held.</param>
/// <param name="Sources">Every source holding bars in the series, ordered.</param>
/// <param name="OpenFindings">Findings nothing has accounted for yet.</param>
/// <param name="LastAttempt">The most recent ingestion attempt, when there was one.</param>
/// <param name="SessionsExpected">
/// Trading days the venue calendar records between the first and last session,
/// or null when that was not measured.
/// </param>
/// <param name="MissingSessions">
/// Of those, the sessions with no bar, or null when that was not measured.
/// </param>
/// <param name="Note">Why a figure is absent or a state was reached, when that needs saying.</param>
public sealed record PriceCoverage(
    BarInterval Interval,
    CoverageState State,
    DateOnly? FirstSession,
    DateOnly? LastSession,
    int Bars,
    IReadOnlyList<SourceCode> Sources,
    int OpenFindings,
    IngestionAttempt? LastAttempt,
    int? SessionsExpected,
    int? MissingSessions,
    string? Note);

/// <summary>
/// One ingestion attempt, reduced to what coverage reports about it.
/// </summary>
/// <param name="Interval">The resolution it was for.</param>
/// <param name="Source">The source it asked.</param>
/// <param name="StartedAtUtc">When it started.</param>
/// <param name="CompletedAtUtc">When it finished, or null while it runs.</param>
/// <param name="Outcome">How it ended.</param>
public sealed record IngestionAttempt(
    BarInterval Interval,
    SourceCode Source,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IngestionOutcome Outcome);

/// <summary>
/// The corporate actions recorded against an instrument.
/// </summary>
/// <remarks>
/// Counted over every recorded action, cancelled ones included — the same set
/// the corporate-actions endpoint lists — with the cancelled count beside it so
/// the two can be told apart. The actions without an announcement date are
/// counted because a point-in-time read cannot place them: a strict read
/// withholds them and a permissive one applies them early.
/// </remarks>
/// <param name="State">Held or not held.</param>
/// <param name="Count">How many actions are recorded.</param>
/// <param name="Cancelled">Of those, how many the issuer called off.</param>
/// <param name="WithoutAnnouncementDate">Of those, how many have no announcement date.</param>
/// <param name="FirstExDate">The earliest ex-date, when any action is recorded.</param>
/// <param name="LastExDate">The latest ex-date, when any action is recorded.</param>
/// <param name="Note">Why the state was reached, when that needs saying.</param>
public sealed record CorporateActionCoverage(
    CoverageState State,
    int Count,
    int Cancelled,
    int WithoutAnnouncementDate,
    DateOnly? FirstExDate,
    DateOnly? LastExDate,
    string? Note);

/// <summary>
/// An instrument's membership of one universe, and how much of time that
/// statement covers.
/// </summary>
/// <remarks>
/// <para>
/// The declared window is reported beside the spells because it is what gives
/// an absence meaning (ADR-020). Inside <see cref="DeclaredFrom"/> to
/// <see cref="DeclaredUntil"/>, a date with no spell is a date the instrument
/// was not a member. Outside it, a date with no spell is a date nobody sourced,
/// and says nothing.
/// </para>
/// <para>
/// So <see cref="CoverageState.NotHeld"/> means "not a member at any time
/// inside the declared window" and nothing about the time outside it, and a
/// universe that declared no window can only ever be
/// <see cref="CoverageState.Held"/> or <see cref="CoverageState.NotMeasured"/>.
/// </para>
/// </remarks>
/// <param name="Code">The universe's code.</param>
/// <param name="Name">The universe's name.</param>
/// <param name="State">Held, not held, or not measured.</param>
/// <param name="DeclaredFrom">
/// The first date whose membership the universe claims to know, or null when
/// it claims nothing.
/// </param>
/// <param name="DeclaredUntil">
/// The date the claim ends, exclusive; null when the claim runs on, or when
/// there is no claim.
/// </param>
/// <param name="Spells">The instrument's spells in the universe, oldest first.</param>
/// <param name="Note">Which part of time the state speaks for.</param>
public sealed record UniverseMembershipCoverage(
    UniverseCode Code,
    string Name,
    CoverageState State,
    DateOnly? DeclaredFrom,
    DateOnly? DeclaredUntil,
    IReadOnlyList<UniverseSpell> Spells,
    string Note);

/// <summary>
/// A dataset reported by state alone, with the reason for it.
/// </summary>
/// <param name="State">The state.</param>
/// <param name="Note">Why.</param>
public sealed record DatasetCoverage(CoverageState State, string Note);
