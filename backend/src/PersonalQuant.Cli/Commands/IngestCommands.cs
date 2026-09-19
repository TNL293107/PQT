using System.Globalization;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Runs the ingestion pipeline on demand.
/// </summary>
/// <remarks>
/// <para>
/// Both verbs call <see cref="IMarketDataIngestionService.IngestAsync"/> and
/// nothing else. <c>backfill</c> is a loop over <c>run</c> rather than a second
/// pipeline: the service already truncates a long range to what one call may
/// carry and advances the checkpoint to the last bar actually stored, so
/// repetition is all a backfill is.
/// </para>
/// <para>
/// The run this produces is indistinguishable from one the scheduled host
/// produces, because it is the same run. That is the point of the group — the
/// only trigger before it was a timer, and a first real backfill therefore had
/// to be spelled as configuration and a restart.
/// </para>
/// </remarks>
/// <param name="ingestion">The pipeline, constructed once the arguments hold.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="universes">Answers who belonged to a universe on a date.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class IngestCommands(
    Lazy<IMarketDataIngestionService> ingestion,
    Lazy<IInstrumentResolver> instruments,
    Lazy<IUniverseCatalog> universes,
    Output output)
{
    /// <summary>
    /// How many passes one backfill may make before it stops and says so.
    /// </summary>
    /// <remarks>
    /// A bound, not a target. The loop stops on its own when a pass covers the
    /// same range as the one before it, which is what a source with no more
    /// data looks like; this is the second stop for the case nobody predicted.
    /// </remarks>
    private const int DefaultMaxPasses = 200;

    /// <summary>
    /// Dispatches an <c>ingest</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "run" => RunOnceAsync(command, cancellationToken),
            "backfill" => BackfillAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> RunOnceAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(
                ["instrument", "interval", "source", "from", "to"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!TryReadCommon(command, out var request))
        {
            return ExitCode.Usage;
        }

        if (!command.TryRequired("instrument", out var ticker, out var missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        var instrument = await ResolveAsync(ticker, cancellationToken).ConfigureAwait(false);

        if (instrument is null)
        {
            return ExitCode.Refused;
        }

        var run = await IngestAsync(
                instrument.InstrumentId,
                request with { From = request.From },
                cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return ExitCode.Usage;
        }

        Report(instrument.Ticker.Value, run);

        return run.Outcome == IngestionOutcome.Succeeded ? ExitCode.Ok : ExitCode.Refused;
    }

    private async Task<int> BackfillAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(
                ["instrument", "universe", "as-of", "interval", "source", "from", "to", "max-passes"],
                out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!TryReadCommon(command, out var request))
        {
            return ExitCode.Usage;
        }

        if (!command.TryCount("max-passes", DefaultMaxPasses, out var maxPasses, out var badCount))
        {
            output.Problem(badCount);
            return ExitCode.Usage;
        }

        if (request.From is null)
        {
            // A backfill with no start is a scheduled run in disguise: it would
            // resume from the checkpoint and stop at the last finished period,
            // which is what 'ingest run' already does.
            output.Problem("'ingest backfill' needs --from. Use 'ingest run' to resume.");
            return ExitCode.Usage;
        }

        var named = command.Value("instrument");
        var universe = command.Value("universe");

        if ((named is null) == (universe is null))
        {
            output.Problem("'ingest backfill' needs exactly one of --instrument and --universe.");
            return ExitCode.Usage;
        }

        var targets = named is not null
            ? await ResolveOneAsync(named, cancellationToken).ConfigureAwait(false)
            : await ResolveUniverseAsync(command, universe!, request.From.Value, cancellationToken)
                .ConfigureAwait(false);

        if (targets is null)
        {
            return ExitCode.Refused;
        }

        var refused = 0;

        foreach (var target in targets)
        {
            if (!await BackfillOneAsync(target, request, maxPasses, cancellationToken)
                    .ConfigureAwait(false))
            {
                refused++;
            }
        }

        if (targets.Count > 1)
        {
            output.Blank();
            output.Line(
                $"{Output.Plural(targets.Count - refused, "instrument")} completed, {refused} refused.");
        }

        return refused == 0 ? ExitCode.Ok : ExitCode.Refused;
    }

    /// <summary>
    /// Runs one instrument to the end of the requested range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every pass names its own start: where the pass before it stopped. The
    /// pipeline truncates a range to what the source carries in one call, so a
    /// backfill is a walk across the range one window at a time, and the walk
    /// has to be steered by the range rather than by the checkpoint.
    /// </para>
    /// <para>
    /// It once was steered by the checkpoint: passes after the first left the
    /// start open. The checkpoint sits after the newest bar stored, so on a
    /// series that already held later bars the second pass jumped straight past
    /// the gap it was asked to fill. VN30 names backfilled for July 2026 and
    /// then asked for August 2024 onwards stored 45 bars of the 480 due, and
    /// reported success.
    /// </para>
    /// <para>
    /// The walk ends at the requested end, or, with none, when the pipeline
    /// skips a pass because no finished period is left. A window the source
    /// returned nothing for is walked past: stopping on "stored nothing" would
    /// end a backfill at the first long suspension. A pass that asks for the
    /// same start as the one before it still ends the walk, as a guard against
    /// a range that cannot advance.
    /// </para>
    /// </remarks>
    private async Task<bool> BackfillOneAsync(
        InstrumentSearchResult instrument,
        IngestRequest request,
        int maxPasses,
        CancellationToken cancellationToken)
    {
        var previous = default(DateTimeOffset?);
        var passes = 0;
        var stored = 0;
        var revised = 0;

        var cursor = ToInstant(request.From);
        var end = ToInstant(request.To);

        while (passes < maxPasses)
        {
            var run = await IngestAsync(
                    instrument.InstrumentId,
                    request,
                    cancellationToken,
                    cursor)
                .ConfigureAwait(false);

            if (run is null)
            {
                return false;
            }

            passes++;

            if (run.Outcome != IngestionOutcome.Succeeded)
            {
                Report(instrument.Ticker.Value, run);

                // A skip that says the range is exhausted is how a completed
                // backfill ends, and it is not a failure.
                var exhausted = run.Outcome == IngestionOutcome.Skipped && run.BarsFetched == 0
                    && previous is not null;

                Summarise(instrument.Ticker.Value, passes, stored, revised);

                return exhausted;
            }

            stored += run.BarsStored;
            revised += run.BarsRevised;

            if (previous == run.RequestedFromUtc
                || (end is { } last && run.RequestedToUtc >= last))
            {
                Summarise(instrument.Ticker.Value, passes, stored, revised);
                return true;
            }

            previous = run.RequestedFromUtc;
            cursor = run.RequestedToUtc;
        }

        Summarise(instrument.Ticker.Value, passes, stored, revised);
        output.Problem(
            $"{instrument.Ticker} stopped after {maxPasses} passes without reaching the end of "
                + "the range. Re-run to continue from the checkpoint, or raise --max-passes.");

        return false;
    }

    private async Task<IngestionRun?> IngestAsync(
        InstrumentId instrumentId,
        IngestRequest request,
        CancellationToken cancellationToken,
        DateTimeOffset? fromUtc = null)
    {
        // An instant rather than a date when a backfill is walking: a window
        // can end part-way through a day at an intraday resolution, and
        // rounding the next start down to the day would re-ask for what the
        // pass before it already covered.
        if (!IngestionInstruction.TryCreate(
                instrumentId,
                request.Interval,
                request.Source,
                fromUtc ?? ToInstant(request.From),
                ToInstant(request.To),
                out var instruction,
                out var problem))
        {
            output.Problem(problem);
            return null;
        }

        return await ingestion.Value.IngestAsync(instruction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<InstrumentSearchResult>?> ResolveOneAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveAsync(ticker, cancellationToken).ConfigureAwait(false);

        return instrument is null ? null : [instrument];
    }

    /// <summary>
    /// Reads who belonged to a universe on a date, refusing when nobody knows.
    /// </summary>
    /// <remarks>
    /// The membership is read as of the day the backfill starts, not today.
    /// Backfilling today's constituents over an earlier decade is precisely the
    /// survivorship bias the universe model exists to remove, and it would be
    /// the natural thing for this command to do if the date were not stated.
    /// An unknown membership is refused rather than treated as an empty one.
    /// </remarks>
    private async Task<IReadOnlyList<InstrumentSearchResult>?> ResolveUniverseAsync(
        CommandArguments command,
        string code,
        DateOnly from,
        CancellationToken cancellationToken)
    {
        if (!command.TryDate("as-of", out var asOfOption, out var badDate))
        {
            output.Problem(badDate);
            return null;
        }

        if (!UniverseCode.TryCreate(code, out var universe))
        {
            output.Problem($"'{code}' is not a usable universe code.");
            return null;
        }

        var asOf = asOfOption ?? from;

        var constituents = await universes.Value
            .ConstituentsAsOfAsync(universe, asOf, cancellationToken)
            .ConfigureAwait(false);

        if (!constituents.IsKnown)
        {
            output.Problem(
                $"Membership of {universe} on {asOf:yyyy-MM-dd} is not known "
                    + $"({constituents.UnknownReason}). It has no member list, and an unknown "
                    + "membership is not an empty one — backfilling it would produce a universe "
                    + "that looks sourced and is not.");

            return null;
        }

        var resolved = new List<InstrumentSearchResult>(constituents.Members.Count);

        foreach (var member in constituents.Members)
        {
            if (await instruments.Value.FindByIdAsync(member, cancellationToken).ConfigureAwait(false)
                is { } instrument)
            {
                resolved.Add(instrument);
                continue;
            }

            // The membership names a security the instrument master does not
            // hold. Reported rather than skipped: it means the two imports
            // disagree, and a backfill quietly short by one constituent is the
            // kind of gap nobody finds later.
            output.Problem($"{universe} names instrument {member} which the master does not hold.");
            return null;
        }

        output.Line(
            $"{universe} on {asOf:yyyy-MM-dd}: {Output.Plural(resolved.Count, "constituent")}.");
        output.Blank();

        return resolved;
    }

    private async Task<InstrumentSearchResult?> ResolveAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        var resolution = await instruments.Value
            .ResolveAsync(ticker, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Instrument is { } instrument)
        {
            return instrument;
        }

        output.Problem(
            resolution.Outcome == InstrumentResolutionOutcome.Ambiguous
                ? $"'{resolution.Query}' is listed on more than one venue: "
                    + string.Join(
                        ", ",
                        resolution.Candidates.Select(candidate => candidate.ExchangeCode.Value))
                    + ". This command resolves by ticker alone."
                : $"No instrument currently trades under '{resolution.Query}'.");

        return null;
    }

    private bool TryReadCommon(CommandArguments command, out IngestRequest request)
    {
        request = default!;

        if (!BarIntervalParser.TryParse(command.Value("interval"), out var interval))
        {
            output.Problem(
                $"The interval is not one this system records. Accepted: {BarIntervalParser.DescribeAccepted()}.");
            return false;
        }

        if (!command.TryDate("from", out var from, out var badFrom))
        {
            output.Problem(badFrom);
            return false;
        }

        if (!command.TryDate("to", out var to, out var badTo))
        {
            output.Problem(badTo);
            return false;
        }

        SourceCode? source = null;

        if (command.Value("source") is { } named)
        {
            if (!SourceCode.TryCreate(named, out var parsed))
            {
                output.Problem($"'{named}' is not a usable source code.");
                return false;
            }

            source = parsed;
        }
        else if (command.HasFlag("source"))
        {
            output.Problem("--source was given without a code.");
            return false;
        }

        request = new IngestRequest(interval, source, from, to);
        return true;
    }

    private void Report(string ticker, IngestionRun run)
    {
        var range = string.Create(
            CultureInfo.InvariantCulture,
            $"{run.RequestedFromUtc:yyyy-MM-dd} to {run.RequestedToUtc:yyyy-MM-dd}");

        output.Line(
            $"{ticker} {BarIntervalParser.Describe(run.Interval)} {run.Source} {range} "
                + $"{run.Outcome.ToString().ToLowerInvariant()} "
                + $"fetched {run.BarsFetched} accepted {run.BarsAccepted} rejected {run.BarsRejected} "
                + $"stored {run.BarsStored} revised {run.BarsRevised}");

        if (run.FailureReason is { } reason)
        {
            output.Problem($"{ticker}: {reason}");
        }
    }

    private void Summarise(string ticker, int passes, int stored, int revised)
    {
        output.Line(
            $"{ticker}: {Output.Plural(passes, "pass")}, {Output.Plural(stored, "bar")} stored, "
                + $"{Output.Plural(revised, "bar")} revised.");
    }

    private int Unknown(string verb)
    {
        output.Problem($"'ingest {verb}' is not a command. Try run or backfill.");
        return ExitCode.Usage;
    }

    /// <summary>
    /// Turns a calendar date into the instant the pipeline reads ranges in.
    /// </summary>
    /// <remarks>
    /// Midnight UTC, and stated rather than assumed. A range boundary read in
    /// the operator's local zone would cover a different set of sessions
    /// depending on where the operator was sitting.
    /// </remarks>
    private static DateTimeOffset? ToInstant(DateOnly? date) =>
        date is { } value ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>What both verbs read from the command line in common.</summary>
    /// <param name="Interval">The resolution.</param>
    /// <param name="Source">The named source, or null to let selection decide.</param>
    /// <param name="From">The start, or null to resume from the checkpoint.</param>
    /// <param name="To">The end, or null for the last finished period.</param>
    private sealed record IngestRequest(
        BarInterval Interval,
        SourceCode? Source,
        DateOnly? From,
        DateOnly? To);
}
