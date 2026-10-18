using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.MarketData;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// What PQT holds for one instrument, on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>state</c> is the name of a <see cref="CoverageState"/> —
/// <c>Held</c>, <c>NotHeld</c>, <c>NotMeasured</c> or <c>NotCollected</c> —
/// and a client must render the four differently (ADR-026). In particular a
/// client may not render <c>NotMeasured</c> as <c>NotHeld</c>: the first says
/// nothing is known, the second says something is known to be absent.
/// </para>
/// <para>
/// A count that was not measured is <c>null</c>, never <c>0</c>. Zero missing
/// sessions is a claim; null is the absence of one.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Ticker">Its current ticker.</param>
/// <param name="Name">Its name.</param>
/// <param name="Exchange">Its venue's code.</param>
/// <param name="Prices">One entry per resolution, daily first.</param>
/// <param name="CorporateActions">The corporate actions recorded.</param>
/// <param name="Universes">Every universe PQT defines, ordered by code.</param>
/// <param name="Fundamentals">The financial reports recorded (ADR-029).</param>
public sealed record InstrumentCoverageResponse(
    Guid InstrumentId,
    string Ticker,
    string Name,
    string Exchange,
    IReadOnlyList<PriceCoverageResponse> Prices,
    CorporateActionCoverageResponse CorporateActions,
    IReadOnlyList<UniverseCoverageResponse> Universes,
    FundamentalsCoverageResponse Fundamentals)
{
    /// <summary>Projects the application answer onto the wire contract.</summary>
    /// <param name="coverage">The answer.</param>
    /// <returns>The response representation.</returns>
    public static InstrumentCoverageResponse From(InstrumentCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return new InstrumentCoverageResponse(
            coverage.InstrumentId.Value,
            coverage.Ticker.Value,
            coverage.Name,
            coverage.Exchange.Value,
            [.. coverage.Prices.Select(PriceCoverageResponse.From)],
            CorporateActionCoverageResponse.From(coverage.CorporateActions),
            [.. coverage.Universes.Select(UniverseCoverageResponse.From)],
            FundamentalsCoverageResponse.From(coverage.Fundamentals));
    }
}

/// <summary>
/// One resolution of the price series.
/// </summary>
/// <param name="Interval">The resolution's name, as every other endpoint writes it.</param>
/// <param name="IntervalCode">The short form a reader types: <c>1d</c>, <c>1h</c>, <c>5m</c>.</param>
/// <param name="State">The coverage state.</param>
/// <param name="FirstSession">The first session held.</param>
/// <param name="LastSession">The last session held.</param>
/// <param name="Bars">How many bars are held.</param>
/// <param name="Sources">Every source holding bars in the series.</param>
/// <param name="OpenFindings">Findings nothing has accounted for.</param>
/// <param name="LastAttempt">The most recent ingestion attempt, whatever its outcome.</param>
/// <param name="SessionsExpected">Trading days between the first and last session, when measured.</param>
/// <param name="MissingSessions">Of those, the ones with no bar, when measured.</param>
/// <param name="Note">Why a figure is absent or a state was reached.</param>
public sealed record PriceCoverageResponse(
    string Interval,
    string IntervalCode,
    string State,
    DateOnly? FirstSession,
    DateOnly? LastSession,
    int Bars,
    IReadOnlyList<string> Sources,
    int OpenFindings,
    IngestionAttemptResponse? LastAttempt,
    int? SessionsExpected,
    int? MissingSessions,
    string? Note)
{
    /// <summary>Projects one resolution onto the wire contract.</summary>
    /// <param name="price">The resolution's coverage.</param>
    /// <returns>The response representation.</returns>
    public static PriceCoverageResponse From(PriceCoverage price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return new PriceCoverageResponse(
            price.Interval.ToString(),
            BarIntervalParser.Describe(price.Interval),
            price.State.ToString(),
            price.FirstSession,
            price.LastSession,
            price.Bars,
            [.. price.Sources.Select(source => source.Value)],
            price.OpenFindings,
            price.LastAttempt is { } attempt
                ? new IngestionAttemptResponse(
                    attempt.Source.Value,
                    attempt.StartedAtUtc,
                    attempt.CompletedAtUtc,
                    attempt.Outcome.ToString())
                : null,
            price.SessionsExpected,
            price.MissingSessions,
            price.Note);
    }
}

/// <summary>
/// The most recent ingestion attempt against a resolution.
/// </summary>
/// <param name="Source">The source asked.</param>
/// <param name="StartedAtUtc">When it started.</param>
/// <param name="CompletedAtUtc">When it finished; null while it runs.</param>
/// <param name="Outcome">Running, Succeeded, Failed or Skipped.</param>
public sealed record IngestionAttemptResponse(
    string Source,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Outcome);

/// <summary>
/// The corporate actions recorded against the instrument.
/// </summary>
/// <param name="State">The coverage state.</param>
/// <param name="Count">How many are recorded, cancelled ones included.</param>
/// <param name="Cancelled">Of those, how many were called off.</param>
/// <param name="WithoutAnnouncementDate">Of those, how many have no announcement date.</param>
/// <param name="FirstExDate">The earliest ex-date.</param>
/// <param name="LastExDate">The latest ex-date.</param>
/// <param name="Note">Why the state was reached.</param>
public sealed record CorporateActionCoverageResponse(
    string State,
    int Count,
    int Cancelled,
    int WithoutAnnouncementDate,
    DateOnly? FirstExDate,
    DateOnly? LastExDate,
    string? Note)
{
    /// <summary>Projects the section onto the wire contract.</summary>
    /// <param name="actions">The section.</param>
    /// <returns>The response representation.</returns>
    public static CorporateActionCoverageResponse From(CorporateActionCoverage actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        return new CorporateActionCoverageResponse(
            actions.State.ToString(),
            actions.Count,
            actions.Cancelled,
            actions.WithoutAnnouncementDate,
            actions.FirstExDate,
            actions.LastExDate,
            actions.Note);
    }
}

/// <summary>
/// The instrument's membership of one universe.
/// </summary>
/// <remarks>
/// <c>declaredFrom</c> and <c>declaredUntil</c> are the window the universe
/// claims to know; <c>declaredUntil</c> is exclusive and null when the claim
/// runs on. Both are null when the universe claims nothing, which is what makes
/// a spell-less instrument <c>NotMeasured</c> rather than <c>NotHeld</c>.
/// </remarks>
/// <param name="Code">The universe's code.</param>
/// <param name="Name">The universe's name.</param>
/// <param name="State">The coverage state.</param>
/// <param name="DeclaredFrom">The first date the universe claims to know.</param>
/// <param name="DeclaredUntil">The date the claim ends, exclusive.</param>
/// <param name="Spells">The instrument's spells, oldest first.</param>
/// <param name="Note">Which part of time the state speaks for.</param>
public sealed record UniverseCoverageResponse(
    string Code,
    string Name,
    string State,
    DateOnly? DeclaredFrom,
    DateOnly? DeclaredUntil,
    IReadOnlyList<UniverseSpellResponse> Spells,
    string Note)
{
    /// <summary>Projects one universe onto the wire contract.</summary>
    /// <param name="membership">The universe's section.</param>
    /// <returns>The response representation.</returns>
    public static UniverseCoverageResponse From(UniverseMembershipCoverage membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        return new UniverseCoverageResponse(
            membership.Code.Value,
            membership.Name,
            membership.State.ToString(),
            membership.DeclaredFrom,
            membership.DeclaredUntil,
            [.. membership.Spells.Select(spell => new UniverseSpellResponse(
                spell.EffectiveFrom,
                spell.EffectiveTo,
                spell.AnnouncedOn,
                spell.RemovalAnnouncedOn))],
            membership.Note);
    }
}

/// <summary>
/// One spell of membership, half-open.
/// </summary>
/// <param name="From">The first date of membership.</param>
/// <param name="To">The date it ended, exclusive; null while still a member.</param>
/// <param name="AnnouncedOn">When the admission was published, when known.</param>
/// <param name="RemovalAnnouncedOn">When the removal was published, when known.</param>
public sealed record UniverseSpellResponse(
    DateOnly From,
    DateOnly? To,
    DateOnly? AnnouncedOn,
    DateOnly? RemovalAnnouncedOn);

/// <summary>
/// A dataset reported by state alone.
/// </summary>
/// <param name="State">The coverage state.</param>
/// <param name="Note">Why.</param>
public sealed record DatasetCoverageResponse(string State, string Note);

/// <summary>The financial reports recorded against an instrument.</summary>
/// <param name="State">Held, NotHeld or NotCollected.</param>
/// <param name="Reports">Reports recorded.</param>
/// <param name="WithoutValues">Of those, disclosed with an empty form.</param>
/// <param name="FirstPeriod">The earliest quarter a report closes on, such as <c>2021Q4</c>.</param>
/// <param name="LastPeriod">The latest quarter a report closes on.</param>
/// <param name="LastDisclosedOn">The latest disclosure date.</param>
/// <param name="Sources">The disclosure sources.</param>
/// <param name="Note">Why a figure is absent or a state was reached.</param>
public sealed record FundamentalsCoverageResponse(
    string State,
    int Reports,
    int WithoutValues,
    string? FirstPeriod,
    string? LastPeriod,
    DateOnly? LastDisclosedOn,
    IReadOnlyList<string> Sources,
    string? Note)
{
    /// <summary>Projects the application answer onto the wire contract.</summary>
    /// <param name="coverage">The answer.</param>
    /// <returns>The response representation.</returns>
    public static FundamentalsCoverageResponse From(FundamentalsCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return new FundamentalsCoverageResponse(
            coverage.State.ToString(),
            coverage.Reports,
            coverage.WithoutValues,
            coverage.FirstPeriod?.ToString(),
            coverage.LastPeriod?.ToString(),
            coverage.LastDisclosedOn,
            [.. coverage.Sources.Select(source => source.Value)],
            coverage.Note);
    }
}
