using System.Globalization;
using System.Reflection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Cli.CommandLine;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Answers what state this deployment is actually in.
/// </summary>
/// <remarks>
/// <para>
/// Both verbs exist because of degradations that are correct and silent. A
/// database behind the build answers every query; a calendar that has run out
/// reports completeness as unmeasured rather than wrong. Neither raises an
/// error, neither fails a health check, and neither announces itself — so the
/// only way to know is to be able to ask.
/// </para>
/// <para>
/// Read-only, both of them. Asking what state a deployment is in must never
/// change it: the reason to ask is not knowing, and a question that migrated a
/// schema as a side effect would be the worst possible answer.
/// </para>
/// </remarks>
/// <param name="schema">Reads what the database has applied.</param>
/// <param name="calendar">Reads how far each venue's calendar reaches.</param>
/// <param name="clock">Supplies today, for measuring what remains.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class DeploymentCommands(
    Lazy<ISchemaState> schema,
    Lazy<ITradingCalendar> calendar,
    Lazy<IClock> clock,
    Output output)
{
    /// <summary>
    /// How far ahead a calendar running out is worth saying out loud.
    /// </summary>
    /// <remarks>
    /// Vietnam's next-year holiday schedule is published late in the year
    /// before, so a quarter's notice is roughly the point at which the notice
    /// exists and the transcription can actually be done. Warning earlier would
    /// be warning about something nobody can act on yet.
    /// </remarks>
    private const int NoticeDays = 90;

    /// <summary>
    /// Dispatches a <c>schema</c> or <c>calendar</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return (command.Group, command.Verb) switch
        {
            ("schema", "status") => SchemaStatusAsync(command, cancellationToken),
            ("calendar", "status") => CalendarStatusAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command)),
        };
    }

    private async Task<int> SchemaStatusAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        var comparison = await schema.Value.ReadAsync(cancellationToken).ConfigureAwait(false);

        const int Width = 20;

        // The build's own version, beside what the database holds. A pending
        // list says the database is behind the build; it says nothing about the
        // build being behind the source, and that was the other half of the
        // drift — an image two weeks old against a database nine migrations
        // older still, each of them internally consistent.
        output.Field("Build", DescribeBuild(), Width);
        output.Field(
            "Migrations applied",
            comparison.AppliedCount.ToString(CultureInfo.InvariantCulture),
            Width);
        output.Field("Last applied", comparison.LastApplied ?? Output.Unknown, Width);

        if (comparison.IsUpToDate)
        {
            output.Field("Pending", "none", Width);
            output.Blank();
            output.Line("The database holds the schema this build expects.");

            return ExitCode.Ok;
        }

        output.Field(
            "Pending",
            comparison.Pending.Count.ToString(CultureInfo.InvariantCulture),
            Width);

        output.Blank();

        foreach (var migration in comparison.Pending)
        {
            output.Line($"  {migration}");
        }

        output.Blank();

        output.Problem(
            comparison.IsEmpty
                ? "This database has never been migrated. It is not a deployment that has "
                    + "fallen behind; it is one that was never initialised."
                : $"The database is {Output.Plural(comparison.Pending.Count, "migration")} behind "
                    + "this build. Every read and write is running against a schema this build "
                    + "was not compiled for.");

        return ExitCode.Refused;
    }

    private async Task<int> CalendarStatusAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        var coverage = await calendar.Value
            .ListCoverageAsync(cancellationToken)
            .ConfigureAwait(false);

        if (coverage.Count == 0)
        {
            output.Line("No venue is recorded.");
            return ExitCode.Ok;
        }

        var today = DateOnly.FromDateTime(clock.Value.UtcNow.UtcDateTime);

        output.Table(
            ["VENUE", "COVERED THROUGH", "DAYS LEFT", "STATE"],
            [.. coverage.Select(entry => (IReadOnlyList<string>)
            [
                entry.Code.Value,
                entry.Through?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Output.Unknown,
                entry.DaysRemaining(today)?.ToString(CultureInfo.InvariantCulture) ?? Output.Unknown,
                Describe(entry, today),
            ])]);

        var lapsed = coverage.Where(entry => entry.IsRecorded && !entry.Covers(today)).ToList();
        var expiring = coverage
            .Where(entry => entry.Covers(today) && entry.DaysRemaining(today) <= NoticeDays)
            .ToList();
        var unrecorded = coverage.Where(entry => !entry.IsRecorded).ToList();

        output.Blank();
        output.Line(
            "Completeness is measured against this calendar. Past the date a venue is covered "
                + "through, a real holiday and a missing session become indistinguishable, so "
                + "completeness is reported as unknown rather than computed wrongly.");

        if (unrecorded.Count > 0)
        {
            // Not a failure on its own. No claim was ever made about these
            // venues, which is a different state from a claim that expired, and
            // the two must not be collapsed.
            output.Blank();
            output.Line(
                $"No calendar is recorded for {Names(unrecorded)}. Completeness has never been "
                    + "measurable for them, which is the honest state and not a regression.");
        }

        foreach (var entry in expiring)
        {
            output.Problem(
                $"{entry.Code} runs out on {entry.Through:yyyy-MM-dd}, in "
                    + $"{Output.Plural(entry.DaysRemaining(today)!.Value, "day")}. The next year's "
                    + "schedule cannot be derived — Tet is lunar and substitute days are set by "
                    + "annual decree — so it has to be transcribed from the exchange's notice.");
        }

        if (lapsed.Count == 0)
        {
            return ExitCode.Ok;
        }

        output.Problem(
            $"Calendar coverage has lapsed for {Names(lapsed)}. Every completeness figure for a "
                + "session after that date is now reported as unmeasured.");

        return ExitCode.Refused;
    }

    private static string Describe(CalendarCoverage entry, DateOnly today)
    {
        if (!entry.IsRecorded)
        {
            return "not recorded";
        }

        if (!entry.Covers(today))
        {
            return "lapsed";
        }

        return entry.DaysRemaining(today) <= NoticeDays ? "expiring" : "covered";
    }

    private static string Names(IEnumerable<CalendarCoverage> entries) =>
        string.Join(", ", entries.Select(entry => entry.Code.Value));

    /// <summary>
    /// Renders the running build's own version.
    /// </summary>
    /// <remarks>
    /// The informational version carries the source revision when the build was
    /// produced by CI, which is what turns "is this image current?" from a
    /// guess into a comparison against the repository.
    /// </remarks>
    private static string DescribeBuild() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Output.Unknown;

    private int Unknown(CommandArguments command)
    {
        output.Problem($"'{command.Group} {command.Verb}' is not a command. Try status.");
        return ExitCode.Usage;
    }
}
