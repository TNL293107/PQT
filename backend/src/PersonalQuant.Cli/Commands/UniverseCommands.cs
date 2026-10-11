using System.Globalization;
using PersonalQuant.Application.Breadth;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Lists the universes a deployment defines, and counts how one moved.
/// </summary>
/// <remarks>
/// <para>
/// <c>breadth</c> prints what <see cref="IMarketBreadthService"/> counted and
/// nothing it did not: the summary line, then every member sorted by change.
/// The arrows carry direction in text, because a terminal piped to a file has
/// no colour, and a decline that only showed as red would vanish from it.
/// </para>
/// <para>
/// A member whose previous close was restated for an action going ex that day
/// says so on its row. Breadth on adjusted closes differs from a broker's board
/// on an ex-date (ADR-026), and an operator comparing the two has to be able to
/// see why.
/// </para>
/// </remarks>
/// <param name="directory">Lists universes, constructed once the arguments hold.</param>
/// <param name="breadth">Counts breadth, constructed once the arguments hold.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class UniverseCommands(
    Lazy<IUniverseDirectory> directory,
    Lazy<IMarketBreadthService> breadth,
    Output output)
{
    private const string Up = "▲";
    private const string Down = "▼";
    private const string Flat = "=";
    private const string Missing = "·";

    /// <summary>
    /// Dispatches a <c>universe</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "list" => ListAsync(command, cancellationToken),
            "breadth" => BreadthAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> ListAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 0)
        {
            output.Problem("'universe list' takes no operands.");
            return ExitCode.Usage;
        }

        var listing = await directory.Value.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listing.Universes.Count == 0)
        {
            output.Line("No universe is defined.");
            return ExitCode.Ok;
        }

        output.Table(
            ["CODE", "KIND", "COVERAGE", $"MEMBERS {Day(listing.AsOf)}", "NAME"],
            [.. listing.Universes.Select(universe => (IReadOnlyList<string>)
            [
                universe.Code.Value,
                universe.Kind.ToString(),
                Coverage(universe.Coverage),
                universe.MemberCount?.ToString(CultureInfo.InvariantCulture) ?? Output.Unknown,
                universe.Name,
            ])]);

        return ExitCode.Ok;
    }

    private async Task<int> BreadthAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["on"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1)
        {
            output.Problem("'universe breadth' needs exactly one universe code, such as VN30.");
            return ExitCode.Usage;
        }

        if (!UniverseCode.TryCreate(command.Operands[0], out var code))
        {
            output.Problem($"'{command.Operands[0]}' is not a universe code.");
            return ExitCode.Usage;
        }

        if (!command.TryDate("on", out var session, out var badDate))
        {
            output.Problem(badDate);
            return ExitCode.Usage;
        }

        var result = await breadth.Value.ComputeAsync(code, session, cancellationToken).ConfigureAwait(false);

        if (result.Breadth is not { } counted)
        {
            output.Problem(result.Detail);
            return ExitCode.Refused;
        }

        Render(counted);
        return ExitCode.Ok;
    }

    private void Render(MarketBreadth counted)
    {
        output.Line(
            $"{counted.Code} {Day(counted.Session)}  {Up} {counted.Advancing}  {Down} {counted.Declining}  "
                + $"{Flat} {counted.Unchanged}  no data {counted.NoData}");

        if (counted.Members.Count == 0)
        {
            output.Line($"{counted.Code} had no members on {Day(counted.Session)}.");
            return;
        }

        output.Line(
            $"volume  {Up} {Number(counted.AdvancingVolume)}  {Down} {Number(counted.DecliningVolume)}  "
                + $"{Flat} {Number(counted.UnchangedVolume)}");
        output.Line(
            "against "
                + string.Join(", ", counted.Venues.Select(venue => $"{venue.Code} {Day(venue.PreviousSession)}")));

        if (counted.SessionWasDefaulted)
        {
            output.Line("(the latest session any member has a bar for; name another with --on yyyy-MM-dd)");
        }

        output.Blank();
        output.Table(
            ["", "TICKER", "CHANGE", "CLOSE", "PREV", "VOLUME", "NOTE"],
            [.. Sorted(counted.Members).Select(Row)]);
    }

    /// <summary>
    /// Orders members by change, largest first, with no-data rows last.
    /// </summary>
    /// <remarks>
    /// Ties broken by ticker, so the same count prints the same way twice.
    /// </remarks>
    private static IEnumerable<BreadthMember> Sorted(IReadOnlyList<BreadthMember> members) =>
        members
            .OrderBy(member => member.Change is null)
            .ThenByDescending(member => member.Change ?? 0m)
            .ThenBy(member => member.Ticker.Value, StringComparer.Ordinal);

    private static IReadOnlyList<string> Row(BreadthMember member) =>
    [
        Arrow(member.Category),
        member.Ticker.Value,
        member.Change is { } change
            ? (change * 100m).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%"
            : "no data",
        Price(member.Close),
        Price(member.PreviousClose ?? member.RawPreviousClose),
        member.Volume is { } volume ? Number(volume) : Output.Unknown,
        Note(member),
    ];

    private static string Note(BreadthMember member)
    {
        if (member.AdjustedForAction)
        {
            return $"ex-date: previous close {Price(member.RawPreviousClose)} restated";
        }

        if (member.Category != BreadthCategory.NoData)
        {
            return string.Empty;
        }

        return member.Close is null
            ? "no bar on the session"
            : $"no bar on {Day(member.PreviousSession)}";
    }

    private static string Arrow(BreadthCategory category) => category switch
    {
        BreadthCategory.Advancing => Up,
        BreadthCategory.Declining => Down,
        BreadthCategory.Unchanged => Flat,
        _ => Missing,
    };

    private static string Coverage(MembershipCoverage? coverage) =>
        coverage switch
        {
            null => "none declared",
            { Until: { } until } => $"{Day(coverage.From)} → {Day(until.AddDays(-1))}",
            _ => $"{Day(coverage.From)} →",
        };

    private static string Price(decimal? value) =>
        value?.ToString("#,##0.####", CultureInfo.InvariantCulture) ?? Output.Unknown;

    private static string Number(long value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private int Unknown(string verb)
    {
        output.Problem($"'universe {verb}' is not a command. Try list or breadth.");
        return ExitCode.Usage;
    }
}
