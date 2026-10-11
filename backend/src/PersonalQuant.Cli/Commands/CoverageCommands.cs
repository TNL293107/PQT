using System.Globalization;
using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Prints what PQT holds for one instrument: <c>pqt coverage &lt;TICKER&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The command group has no verb; the word after <c>coverage</c> is the
/// security, the same way the console reads <c>FPT COV</c>. A ticker resolves
/// through the instrument master and an instrument identifier is accepted as
/// is, which is the one unambiguous way to name a security whose ticker is
/// live on two venues.
/// </para>
/// <para>
/// Every state is printed with a mark and a word, so the four stay apart on a
/// terminal that mangles the mark. <c>? not measured</c> and
/// <c>✗ not held</c> in particular are never the same line (ADR-026): the
/// first says PQT cannot tell, the second says PQT knows it holds none.
/// </para>
/// </remarks>
/// <param name="coverage">The coverage query, constructed once the arguments hold.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class CoverageCommands(
    Lazy<IInstrumentCoverageService> coverage,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>
    /// Runs <c>coverage &lt;TICKER&gt;</c>.
    /// </summary>
    /// <param name="command">The parsed command line; the verb is the security.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Validate(["exchange"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count > 0)
        {
            output.Problem(
                $"'coverage' takes one security, but was given '{command.Verb} "
                    + $"{string.Join(' ', command.Operands)}'. Name it by ticker or by identifier.");

            return ExitCode.Usage;
        }

        ExchangeCode? exchange = null;

        if (command.Value("exchange") is { } venue && !ExchangeCode.TryCreate(venue, out exchange))
        {
            output.Problem($"'--exchange {venue}' is not a venue code.");
            return ExitCode.Usage;
        }

        var instrumentId = await ResolveAsync(command.Verb, exchange, cancellationToken)
            .ConfigureAwait(false);

        if (instrumentId is not { } id)
        {
            return ExitCode.Refused;
        }

        var described = await coverage.Value
            .DescribeAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (described is null)
        {
            output.Problem($"No instrument exists with the identifier {id}.");
            return ExitCode.Refused;
        }

        CoverageReport.Write(output, described);

        return ExitCode.Ok;
    }

    private async Task<InstrumentId?> ResolveAsync(
        string security,
        ExchangeCode? exchange,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(security, out var identifier))
        {
            return new InstrumentId(identifier);
        }

        var resolution = await instruments.Value
            .ResolveAsync(security, exchange, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Instrument is { } instrument)
        {
            return instrument.InstrumentId;
        }

        output.Problem(
            resolution.Outcome == InstrumentResolutionOutcome.Ambiguous
                ? $"'{resolution.Query}' is listed on more than one venue: "
                    + string.Join(
                        ", ",
                        resolution.Candidates.Select(candidate => candidate.ExchangeCode.Value))
                    + ". Name one with --exchange."
                : $"No instrument currently trades under '{resolution.Query}'.");

        return null;
    }
}

/// <summary>
/// Renders an <see cref="InstrumentCoverage"/> as an aligned table.
/// </summary>
/// <remarks>
/// Rendering only. Every state and every sentence explaining one was decided
/// by the application query; this decides columns and marks.
/// </remarks>
internal static class CoverageReport
{
    /// <summary>
    /// Writes the report.
    /// </summary>
    /// <param name="output">Where to write it.</param>
    /// <param name="coverage">The coverage to render.</param>
    public static void Write(Output output, InstrumentCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(coverage);

        output.Line($"{coverage.Ticker}  {coverage.Name}  {coverage.Exchange}  {coverage.InstrumentId}");
        output.Blank();

        var rows = new List<IReadOnlyList<string>>();

        foreach (var price in coverage.Prices)
        {
            Add(rows, $"Price {BarIntervalParser.Describe(price.Interval)}", price.State, PriceExtent(price), PriceDetail(price), price.Note);
        }

        var actions = coverage.CorporateActions;
        Add(rows, "Corporate actions", actions.State, Span(actions.FirstExDate, actions.LastExDate), ActionDetail(actions), actions.Note);

        if (coverage.Universes.Count == 0)
        {
            Add(rows, "Universe membership", CoverageState.NotMeasured, string.Empty, string.Empty, "No universe is defined.");
        }

        foreach (var universe in coverage.Universes)
        {
            Add(rows, $"Universe {universe.Code}", universe.State, SpellExtent(universe), SpellDetail(universe), universe.Note);
        }

        Add(rows, "Fundamentals", coverage.Fundamentals.State, string.Empty, coverage.Fundamentals.Note, note: null);

        output.Table(["DATASET", "STATE", "EXTENT", "DETAIL"], rows);
        output.Blank();
        output.Line(
            "✓ held · ✗ not held (known absent) · ? not measured (cannot say) · — not collected yet");
    }

    /// <summary>The mark and word a state is printed as.</summary>
    /// <param name="state">The state.</param>
    /// <returns>Both, so the state survives a terminal that drops the mark.</returns>
    public static string Describe(CoverageState state) => state switch
    {
        CoverageState.Held => "✓ held",
        CoverageState.NotHeld => "✗ not held",
        CoverageState.NotMeasured => "? not measured",
        CoverageState.NotCollected => "— not collected",
        _ => state.ToString(),
    };

    private static void Add(
        List<IReadOnlyList<string>> rows,
        string dataset,
        CoverageState state,
        string extent,
        string detail,
        string? note)
    {
        // Beside the facts when there are none, otherwise on a row of its own
        // rather than at the end of a longer cell: the note is the sentence
        // that says which part of the answer is a fact and which is silence,
        // and it should not be the part that wraps away.
        if (string.IsNullOrEmpty(detail) && !string.IsNullOrEmpty(note))
        {
            rows.Add([dataset, Describe(state), extent, note]);
            return;
        }

        rows.Add([dataset, Describe(state), extent, detail]);

        if (!string.IsNullOrEmpty(note))
        {
            rows.Add([string.Empty, string.Empty, string.Empty, note]);
        }
    }

    private static string PriceExtent(PriceCoverage price) => Span(price.FirstSession, price.LastSession);

    private static string PriceDetail(PriceCoverage price)
    {
        var parts = new List<string>();

        if (price.State == CoverageState.Held)
        {
            parts.Add(price.Bars == 1 ? "1 bar" : $"{Count(price.Bars)} bars");
            parts.Add(string.Join(",", price.Sources.Select(source => source.Value)));
            // When the count is absent the server's note says why, on its
            // own line; repeating it here would say it twice.
            if (price.MissingSessions is { } missing && price.SessionsExpected is { } expected)
            {
                parts.Add($"{Count(missing)} of {Count(expected)} sessions missing");
            }
        }

        if (price.OpenFindings > 0)
        {
            parts.Add(Output.Plural(price.OpenFindings, "open finding"));
        }

        if (price.LastAttempt is { } attempt)
        {
            parts.Add($"last attempt {Day(attempt.StartedAtUtc)} {attempt.Source} {attempt.Outcome}");
        }

        return string.Join(" · ", parts);
    }

    private static string ActionDetail(CorporateActionCoverage actions)
    {
        if (actions.State != CoverageState.Held)
        {
            return string.Empty;
        }

        var parts = new List<string> { Output.Plural(actions.Count, "action") };

        if (actions.WithoutAnnouncementDate > 0)
        {
            parts.Add($"{Count(actions.WithoutAnnouncementDate)} without announcement date");
        }

        if (actions.Cancelled > 0)
        {
            parts.Add($"{Count(actions.Cancelled)} cancelled");
        }

        return string.Join(" · ", parts);
    }

    private static string SpellExtent(UniverseMembershipCoverage universe) =>
        universe.Spells.Count == 0
            ? string.Empty
            : $"{Day(universe.Spells[0].EffectiveFrom)} → "
                + (universe.Spells[^1].EffectiveTo is { } end ? Day(end) : "now");

    private static string SpellDetail(UniverseMembershipCoverage universe) =>
        universe.Spells.Count switch
        {
            0 => string.Empty,
            1 => "1 spell",
            var count => $"{count} spells — gaps between them are recorded exits",
        };

    private static string Span(DateOnly? first, DateOnly? last) =>
        first is { } from && last is { } to ? $"{Day(from)} → {Day(to)}" : string.Empty;

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
