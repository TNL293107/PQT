using System.Globalization;
using System.Text.Json;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Builds the canonical research dataset, and checks one still is what it
/// claims to be.
/// </summary>
/// <remarks>
/// <para>
/// An operator command rather than an endpoint. An export walks every
/// constituent of a universe across a date range and writes files to the
/// deployment's own disk, which is a scheduled job's shape and not a web
/// request's, and it runs with the environment the deployment actually has.
/// </para>
/// <para>
/// <c>verify</c> exists because a recorded hash that is never checked is a
/// comment. A dataset whose file no longer matches its manifest must be a hard
/// error, since a reproducible result computed from a silently changed input is
/// worse than no result.
/// </para>
/// </remarks>
/// <param name="datasets">The export service, constructed once the arguments hold.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class DatasetCommands(Lazy<IDatasetExportService> datasets, Output output)
{
    /// <summary>Width the manifest summary aligns its labels to.</summary>
    private const int LabelWidth = 22;

    /// <summary>The options a request file decides, which may not be given beside it.</summary>
    private static readonly string[] RequestOptions =
        ["universe", "as-of", "interval", "from", "to", "raw", "known-as-of", "policy", "history", "fundamentals"];

    /// <summary>How a request file is spelled: the manifest's snake case, every field required.</summary>
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Dispatches a <c>dataset</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "export" => ExportAsync(command, cancellationToken),
            "verify" => VerifyAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> ExportAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(
                ["universe", "as-of", "interval", "from", "to", "raw", "known-as-of", "policy", "history", "fundamentals", "request"],
                out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Value("request") is { } file)
        {
            return await ExportRequestAsync(command, file, cancellationToken).ConfigureAwait(false);
        }

        if (!command.TryRequired("universe", out var universe, out var missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        if (!BarIntervalParser.TryParse(command.Value("interval"), out var interval))
        {
            output.Problem(
                $"The interval is not one this system records. Accepted: {BarIntervalParser.DescribeAccepted()}.");

            return ExitCode.Usage;
        }

        if (!AnnouncementPolicyParser.TryParse(command.Value("policy") ?? "strict", out var policy))
        {
            output.Problem(
                $"The announcement policy is not one this system understands. Accepted: {AnnouncementPolicyParser.DescribeAccepted()}.");

            return ExitCode.Usage;
        }

        if (!command.TryDate("from", out var from, out var badFrom)
            || !command.TryDate("to", out var to, out badFrom)
            || !command.TryDate("as-of", out var asOf, out badFrom)
            || !command.TryDate("known-as-of", out var knownAsOf, out badFrom))
        {
            output.Problem(badFrom);
            return ExitCode.Usage;
        }

        if (!TryHistory(command, out var history, out var badHistory))
        {
            output.Problem(badHistory);
            return ExitCode.Usage;
        }

        if (from is not { } fromDate)
        {
            output.Problem("--from is required. An export with no start would read the whole series.");
            return ExitCode.Usage;
        }

        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        UniverseCode code;

        try
        {
            code = UniverseCode.Create(universe);
        }
        catch (DomainValidationException invalid)
        {
            output.Problem(invalid.Message);
            return ExitCode.Usage;
        }

        var adjusted = !command.HasFlag("raw");
        DateTimeOffset? knownAsOfUtc = knownAsOf is { } cut ? Instant(cut) : null;

        // Point-in-time unless a date is named. Without --as-of each row is
        // kept only where its instrument was a member that session, which is the
        // survivorship-free reading and the one a backtest across a review
        // needs. Naming a date asks for one constituent set applied to the whole
        // window - the older behaviour, kept, and chosen rather than defaulted
        // into, for the reason the strict policy is the default: a dataset is
        // read by things nobody has written yet.
        DatasetRequest? request;
        string? invalidRequest;

        var valid = asOf is { } universeAsOf
            ? DatasetRequest.TryCreate(
                code,
                universeAsOf,
                interval,
                fromDate,
                toDate,
                out request,
                out invalidRequest,
                adjusted,
                knownAsOfUtc,
                policy,
                history,
                command.HasFlag("fundamentals"))
            : DatasetRequest.TryCreatePointInTime(
                code,
                interval,
                fromDate,
                toDate,
                out request,
                out invalidRequest,
                adjusted,
                knownAsOfUtc,
                policy,
                history,
                command.HasFlag("fundamentals"));

        if (!valid || request is null)
        {
            output.Problem(invalidRequest ?? "The export request is not valid.");
            return ExitCode.Usage;
        }

        var export = await datasets.Value
            .ExportAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (export.Manifest is not { } manifest)
        {
            output.Problem(export.Problem ?? "The export produced nothing.");
            return ExitCode.Refused;
        }

        Describe(manifest, export.InstrumentsWithoutBars);
        return ExitCode.Ok;
    }

    /// <summary>
    /// Exports from a labelled-dataset request file (ADR-034): the request
    /// names everything, so no other option may be given beside it.
    /// </summary>
    private async Task<int> ExportRequestAsync(CommandArguments command, string file, CancellationToken cancellationToken)
    {
        if (RequestOptions.FirstOrDefault(command.HasFlag) is { } extra)
        {
            output.Problem($"--{extra} cannot be given with --request: the request names the export.");
            return ExitCode.Usage;
        }

        LabelledDatasetRequest? labelled;

        try
        {
            labelled = JsonSerializer.Deserialize<LabelledDatasetRequest>(
                await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false),
                RequestJson);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            output.Problem($"The request file could not be read: {error.Message}");
            return ExitCode.Usage;
        }

        if (labelled is null)
        {
            output.Problem("The request file is empty.");
            return ExitCode.Usage;
        }

        if (!DatasetRequest.TryResolve(labelled, out var request, out var refused))
        {
            output.Problem(refused);
            return ExitCode.Usage;
        }

        var export = await datasets.Value.ExportAsync(request, cancellationToken).ConfigureAwait(false);

        if (export.Manifest is not { } manifest)
        {
            output.Problem(export.Problem ?? "The export produced nothing.");
            return ExitCode.Refused;
        }

        Describe(manifest, export.InstrumentsWithoutBars);
        return ExitCode.Ok;
    }

    private async Task<int> VerifyAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(["version"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1)
        {
            output.Problem("pqt dataset verify <DATASET-ID> [--version N]");
            return ExitCode.Usage;
        }

        if (!command.TryCount("version", 1, out var version, out var badVersion))
        {
            output.Problem(badVersion);
            return ExitCode.Usage;
        }

        var result = await datasets.Value
            .VerifyAsync(command.Operands[0], version, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsIntact)
        {
            // The verb agrees with the count, not the noun. "1 file match" is
            // the same slip as "4 passs", and it reaches an operator's terminal
            // just as directly.
            output.Line(
                $"{result.DatasetId} v{result.Version}: "
                    + $"{Output.Plural(result.FilesChecked, "file")} "
                    + $"{(result.FilesChecked == 1 ? "matches" : "match")} the manifest.");

            return ExitCode.Ok;
        }

        foreach (var line in result.Problems)
        {
            output.Problem(line);
        }

        return ExitCode.Refused;
    }

    /// <summary>
    /// Prints what was written, in the terms the manifest records it.
    /// </summary>
    /// <remarks>
    /// The as-of and the policy are printed even when they are the defaults.
    /// They are what makes one dataset comparable with another, and an operator
    /// reading a terminal scrollback months later has no other way to tell
    /// which reading produced the file.
    /// </remarks>
    private void Describe(DatasetManifest manifest, int withoutBars)
    {
        output.Field("dataset", $"{manifest.DatasetId} v{manifest.DatasetVersion}", LabelWidth);
        output.Field("schema version", manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture), LabelWidth);
        output.Field("content hash", manifest.ContentHash, LabelWidth);
        output.Field(
            "universe",
            manifest.Membership == DatasetMembership.AsOf && manifest.UniverseAsOf is { } asOf
                ? $"{manifest.UniverseCode} as of {Day(asOf)}"
                : $"{manifest.UniverseCode} point-in-time, "
                    + $"{Output.Plural(manifest.Instruments.Sum(instrument => instrument.Spells?.Count ?? 0), "spell")}",
            LabelWidth);
        output.Field("window", $"{Day(manifest.FromDate)} → {Day(manifest.ToDate)} {manifest.Interval}", LabelWidth);
        output.Field(
            "adjustment",
            manifest.Adjusted
                ? $"adjusted, {AnnouncementPolicyParser.Describe(manifest.AnnouncementPolicy)} announcements"
                : "raw",
            LabelWidth);
        output.Field(
            "known as of",
            manifest.KnownAsOfUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "current series",
            LabelWidth);
        output.Field(
            "history",
            manifest.HistorySessions == 0
                ? "none: rows only where each instrument was a member"
                : $"up to {Output.Plural(manifest.HistorySessions, "bar")} before each instrument's first membership session",
            LabelWidth);
        output.Field(
            "fundamentals",
            manifest.Fundamentals is { } fundamentals
                ? $"ratio version {fundamentals.RatioVersion}, {fundamentals.Source}, cut {fundamentals.Cut}, "
                    + $"{Output.Plural((int)fundamentals.Rows, "row")}, "
                    + $"{fundamentals.NotCollected.Count.ToString(CultureInfo.InvariantCulture)} not collected"
                : "none",
            LabelWidth);
        output.Field("instruments", manifest.Instruments.Count.ToString(CultureInfo.InvariantCulture), LabelWidth);
        output.Field("rows", manifest.RowCount.ToString(CultureInfo.InvariantCulture), LabelWidth);
        output.Field("built by", manifest.CreatedByCommit ?? "unrecorded build", LabelWidth);

        output.Blank();

        foreach (var file in manifest.Files)
        {
            output.Line($"{file.Name}  {file.Bytes:N0} bytes  sha256:{file.Sha256}");
        }

        if (withoutBars > 0)
        {
            output.Blank();
            output.Line(
                $"{Output.Plural(withoutBars, "constituent")} had no stored bars over the window and "
                    + "contributed no rows. The dataset covers fewer securities than the universe does.");
        }

        foreach (var source in manifest.Sources)
        {
            output.Line($"{source.Code}: {source.LicenceNote}");
        }

        if (manifest.Fundamentals is { NotCollected.Count: > 0 } gaps)
        {
            output.Blank();

            foreach (var gap in gaps.NotCollected)
            {
                var ticker = manifest.Instruments.FirstOrDefault(instrument => instrument.InstrumentId == gap.InstrumentId)?.TickerAtAsOf;
                output.Line($"{ticker ?? gap.InstrumentId.ToString()}: no fundamentals ({gap.Reason}); no rows.");
            }
        }

        if (manifest.Fundamentals is { } held && !string.Equals(held.Source, "none", StringComparison.Ordinal))
        {
            output.Line($"{held.Source} (fundamentals): {held.LicenceNote}");
        }
    }

    /// <summary>
    /// Reads <c>--history</c>, where zero is a legitimate value.
    /// </summary>
    /// <remarks>
    /// Not <see cref="CommandArguments.TryCount"/>, which refuses zero: a
    /// script that passes its history through as a variable must be able to
    /// pass the default. The upper bound is the request's to state, so it is
    /// stated once.
    /// </remarks>
    private static bool TryHistory(
        CommandArguments command,
        out int history,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? problem)
    {
        history = 0;
        problem = null;

        if (!command.HasFlag("history"))
        {
            return true;
        }

        var value = command.Value("history");

        if (value is not null
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out history))
        {
            return true;
        }

        problem = $"--history {value} is not a whole number of bars. Write it as 0 to {DatasetRequest.MaxHistorySessions}.";
        return false;
    }

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Instant(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private int Unknown(string verb)
    {
        output.Problem($"'{verb}' is not a dataset command. Try export or verify.");
        return ExitCode.Usage;
    }
}
