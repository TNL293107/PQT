using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Reads what the registered sources declare, and answers whether one would be
/// chosen for a request.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is declared by the provider rather than measured against
/// it. Nothing in this group makes a call to a third party, which is why
/// <c>provider check</c> can be run freely against a metered source: it asks
/// the registry, not the vendor.
/// </para>
/// <para>
/// The instrument resolver arrives lazily, and that is not an optimisation.
/// <c>list</c> and <c>show</c> read declarations that live in the composition
/// root and touch no table; resolving the repository eagerly would make them
/// require a reachable, configured database to print what the deployment
/// already knows about itself. The first thing an operator does on a host that
/// will not start is ask what it thinks it has.
/// </para>
/// </remarks>
/// <param name="registry">The registered sources.</param>
/// <param name="instruments">
/// Resolves a ticker to the security it names, constructed only by the verb
/// that needs one.
/// </param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class ProviderCommands(
    IMarketDataProviderRegistry registry,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>
    /// Dispatches a <c>provider</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "list" => Task.FromResult(List(command)),
            "show" => Task.FromResult(Show(command)),
            "check" => CheckAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private int List(CommandArguments command)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (registry.Providers.Count == 0)
        {
            // Not an error. A deployment that has been pointed at no source
            // ingests nothing and records skipped runs saying so, and this is
            // the surface that says which state it is in.
            output.Line("No market data source is registered.");
            return ExitCode.Ok;
        }

        var rows = registry.Providers
            .Select(provider => (IReadOnlyList<string>)
            [
                provider.Code.Value,
                provider.Capability.DisplayName,
                DescribeIntervals(provider.Capability),
                DescribeExchanges(provider.Capability),
                DescribeEarliest(provider.Capability),
                YesNo(provider.Capability.Limitations.AdjustsPricesAtSource),
            ])
            .ToList();

        output.Table(
            ["CODE", "NAME", "INTERVALS", "EXCHANGES", "EARLIEST", "ADJUSTED AT SOURCE"],
            rows);

        return ExitCode.Ok;
    }

    private int Show(CommandArguments command)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1)
        {
            output.Problem("'provider show' needs exactly one source code.");
            return ExitCode.Usage;
        }

        if (!TryResolveProvider(command.Operands[0], out var provider))
        {
            return ExitCode.Refused;
        }

        var capability = provider.Capability;
        const int Width = 24;

        output.Field("Code", capability.Code.Value, Width);
        output.Field("Name", capability.DisplayName, Width);
        output.Field("Intervals", DescribeIntervals(capability), Width);
        output.Field("Exchanges", DescribeExchanges(capability), Width);
        output.Field("Asset types", DescribeAssetTypes(capability), Width);
        output.Field("Earliest available", DescribeEarliest(capability), Width);
        output.Blank();
        output.Field("Turnover", YesNo(capability.ReportedFields.Turnover), Width);
        output.Field("Announcement dates", YesNo(capability.ReportedFields.AnnouncementDates), Width);
        output.Field("Restatements", YesNo(capability.ReportedFields.Restatements), Width);
        output.Blank();
        output.Field("Adjusted at source", YesNo(capability.Limitations.AdjustsPricesAtSource), Width);
        output.Field("Max periods per call", DescribeMaxPeriods(capability), Width);
        output.Field("Minimum call spacing", DescribeSpacing(capability), Width);

        if (capability.Limitations.AdjustsPricesAtSource)
        {
            output.Blank();
            output.Line(
                "This source serves prices already adjusted for corporate actions. A series");
            output.Line(
                "from it is a different dataset from a raw one: the adjusted read returns it");
            output.Line(
                "unrescaled, and ingestion refuses to mix it with a series a raw source holds.");
        }

        return ExitCode.Ok;
    }

    private async Task<int> CheckAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["instrument", "interval", "from"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1)
        {
            output.Problem("'provider check' needs exactly one source code.");
            return ExitCode.Usage;
        }

        if (!command.TryRequired("instrument", out var ticker, out var missing))
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

        if (!command.TryDate("from", out var from, out var badDate))
        {
            output.Problem(badDate);
            return ExitCode.Usage;
        }

        if (!SourceCode.TryCreate(command.Operands[0], out var code))
        {
            output.Problem($"'{command.Operands[0]}' is not a usable source code.");
            return ExitCode.Usage;
        }

        var resolution = await instruments.Value
            .ResolveAsync(ticker, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Instrument is not { } instrument)
        {
            output.Problem(DescribeUnresolved(resolution));
            return ExitCode.Refused;
        }

        // The same call the ingestion pipeline makes, with the same criteria.
        // A check that reasoned about capability itself would be a second
        // implementation of selection, and the two would eventually disagree
        // about the run this command was asked to predict.
        var selection = registry.SelectProvider(
            new ProviderCriteria(interval, code, instrument.ExchangeCode, instrument.AssetType));

        output.Field("Source", code.Value, 12);
        output.Field("Instrument", $"{instrument.Ticker} on {instrument.ExchangeCode}", 12);
        output.Field("Interval", BarIntervalParser.Describe(interval), 12);
        output.Field("Outcome", selection.Outcome.ToString(), 12);

        if (selection.Reason is { } reason)
        {
            output.Field("Reason", reason, 12);
            return ExitCode.Refused;
        }

        ReportClamp(selection.Provider!, from);

        return ExitCode.Ok;
    }

    /// <summary>
    /// Says that a requested start would be moved forward, when the source
    /// declares a floor above it.
    /// </summary>
    /// <remarks>
    /// V4, rendered rather than applied. The pipeline is what clamps the range
    /// and records the clamp on the run; this reports the declaration so the
    /// operator learns it before spending the call rather than afterwards.
    /// </remarks>
    private void ReportClamp(IMarketDataProvider provider, DateOnly? from)
    {
        if (from is not { } requested)
        {
            return;
        }

        if (provider.Capability.EarliestAvailable is not { } earliest)
        {
            output.Field(
                "Range",
                $"{requested:yyyy-MM-dd} onwards; the source states no earliest date, so how far "
                    + "back it answers is unknown.",
                12);

            return;
        }

        output.Field(
            "Range",
            requested < earliest
                ? $"{requested:yyyy-MM-dd} would be clamped forward to {earliest:yyyy-MM-dd}."
                : $"{requested:yyyy-MM-dd} is within the declared coverage from {earliest:yyyy-MM-dd}.",
            12);
    }

    private bool TryResolveProvider(
        string value,
        [NotNullWhen(true)] out IMarketDataProvider? provider)
    {
        provider = null;

        if (!SourceCode.TryCreate(value, out var code))
        {
            output.Problem($"'{value}' is not a usable source code.");
            return false;
        }

        if (registry.TryResolve(code, out var resolved))
        {
            provider = resolved;
            return true;
        }

        var registered = registry.Providers.Count == 0
            ? "none"
            : string.Join(", ", registry.Providers.Select(candidate => candidate.Code.Value));

        output.Problem($"No source is registered under '{code}'. Registered: {registered}.");
        return false;
    }

    private int Unknown(string verb)
    {
        output.Problem($"'provider {verb}' is not a command. Try list, show or check.");
        return ExitCode.Usage;
    }

    private static string DescribeUnresolved(InstrumentResolution resolution) =>
        resolution.Outcome switch
        {
            InstrumentResolutionOutcome.Ambiguous =>
                $"'{resolution.Query}' is listed on more than one venue: "
                    + string.Join(
                        ", ",
                        resolution.Candidates.Select(candidate => candidate.ExchangeCode.Value))
                    + ".",
            _ => $"No instrument currently trades under '{resolution.Query}'.",
        };

    private static string DescribeIntervals(ProviderCapability capability) =>
        string.Join(
            ",",
            capability.Intervals.OrderBy(interval => (int)interval).Select(BarIntervalParser.Describe));

    /// <summary>
    /// Renders a venue set, keeping "no restriction" distinct from "unknown".
    /// </summary>
    /// <remarks>
    /// An empty set is a claim — a directory of CSV files genuinely has no
    /// venue restriction — and it is not the same as a vendor that never said.
    /// Rendering both as blank is exactly the collapse the capability record
    /// exists to prevent.
    /// </remarks>
    private static string DescribeExchanges(ProviderCapability capability) =>
        capability.Exchanges.Count == 0
            ? "any"
            : string.Join(
                ",",
                capability.Exchanges.Select(exchange => exchange.Value).Order(StringComparer.Ordinal));

    private static string DescribeAssetTypes(ProviderCapability capability) =>
        capability.AssetTypes.Count == 0
            ? "any"
            : string.Join(
                ",",
                capability.AssetTypes.Select(type => type.ToString()).Order(StringComparer.Ordinal));

    /// <summary>
    /// Renders the coverage floor, and never renders an unstated one as
    /// unbounded.
    /// </summary>
    private static string DescribeEarliest(ProviderCapability capability) =>
        capability.EarliestAvailable is { } earliest
            ? earliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : Output.Unknown;

    private static string DescribeMaxPeriods(ProviderCapability capability) =>
        capability.Limitations.MaxPeriodsPerCall is { } max
            ? max.ToString(CultureInfo.InvariantCulture)
            : Output.Unknown;

    private static string DescribeSpacing(ProviderCapability capability) =>
        capability.Limitations.MinimumCallSpacing is { } spacing
            ? string.Create(CultureInfo.InvariantCulture, $"{spacing.TotalMilliseconds:0}ms")
            : Output.Unknown;

    private static string YesNo(bool value) => value ? "yes" : "no";
}
