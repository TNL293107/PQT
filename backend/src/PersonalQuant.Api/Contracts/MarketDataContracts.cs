using System.Globalization;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// One bar on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Prices are serialised as JSON numbers, which for a decimal means they cross
/// the wire in their exact decimal form. A client that parses them into a
/// binary float will lose that, which is the client's decision to make; this
/// side does not make it for them by rounding on the way out.
/// </para>
/// <para>
/// The interval travels as its name rather than its minute count. A client
/// reading <c>"OneDay"</c> cannot silently misread it, and a response is
/// legible in a log without a lookup.
/// </para>
/// </remarks>
/// <param name="OpenedAtUtc">The instant the period opened.</param>
/// <param name="Open">The first traded price.</param>
/// <param name="High">The highest traded price.</param>
/// <param name="Low">The lowest traded price.</param>
/// <param name="Close">The last traded price.</param>
/// <param name="Volume">Units traded.</param>
/// <param name="Turnover">
/// Cash value traded, when the source reported it. Never rescaled: it is the
/// cash that actually changed hands rather than a per-share quantity.
/// </param>
/// <param name="Source">Where the bar came from.</param>
/// <param name="Revision">How many times the source has restated the period.</param>
/// <param name="PriceFactor">What the prices were multiplied by. One when raw.</param>
/// <param name="ShareFactor">What the volume was multiplied by. One when raw.</param>
public sealed record BarResponse(
    DateTimeOffset OpenedAtUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Turnover,
    string Source,
    int Revision,
    decimal PriceFactor,
    decimal ShareFactor)
{
    /// <summary>Projects a bar onto the wire contract.</summary>
    /// <param name="bar">The bar to project.</param>
    /// <returns>The response representation.</returns>
    public static BarResponse From(SeriesBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        return new BarResponse(
            bar.OpenedAtUtc,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.Turnover,
            bar.Source.Value,
            bar.Revision,
            bar.PriceFactor,
            bar.ShareFactor);
    }
}

/// <summary>
/// A window of one instrument's series.
/// </summary>
/// <remarks>
/// The applied bound is echoed back so a client can tell a series that ended
/// from one that was truncated. Without it, three hundred bars returned for a
/// ten-year request looks like ten years of history.
/// </remarks>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Interval">The resolution, by name.</param>
/// <param name="Adjusted">
/// Whether the prices were rescaled for corporate actions. Always stated: an
/// adjusted series and a raw one answer different questions, and a client that
/// cannot tell them apart will eventually compare one against the other.
/// </param>
/// <param name="AdjustedBars">
/// How many bars in this response were actually rescaled. Zero on an adjusted
/// series means no action went ex inside the window — not that adjustment was
/// skipped.
/// </param>
/// <param name="Count">How many bars are in this response.</param>
/// <param name="Limit">The bound that was applied.</param>
/// <param name="Bars">The bars, oldest first.</param>
public sealed record BarSeriesResponse(
    Guid InstrumentId,
    string Interval,
    bool Adjusted,
    int AdjustedBars,
    int Count,
    int Limit,
    IReadOnlyList<BarResponse> Bars)
{
    /// <summary>Projects a series onto the wire contract.</summary>
    /// <param name="series">The series to project.</param>
    /// <param name="limit">The bound that was applied.</param>
    /// <returns>The response representation.</returns>
    public static BarSeriesResponse From(BarSeries series, int limit)
    {
        ArgumentNullException.ThrowIfNull(series);

        return new BarSeriesResponse(
            series.InstrumentId.Value,
            series.Interval.ToString(),
            series.Adjusted,
            series.Bars.Count(bar => bar.IsAdjusted),
            series.Bars.Count,
            limit,
            [.. series.Bars.Select(BarResponse.From)]);
    }
}

/// <summary>
/// One ingestion attempt on the wire.
/// </summary>
/// <remarks>
/// The counts are reported separately rather than summed, for the reason they
/// are stored separately: a run that fetched a thousand rows and rejected all
/// of them succeeded, and only the breakdown says so.
/// </remarks>
/// <param name="RunId">The run's identifier.</param>
/// <param name="Source">The source that was read.</param>
/// <param name="Interval">The resolution, by name.</param>
/// <param name="RequestedFromUtc">The inclusive start of the requested range.</param>
/// <param name="RequestedToUtc">The exclusive end of the requested range.</param>
/// <param name="StartedAtUtc">When the attempt started.</param>
/// <param name="CompletedAtUtc">When it finished, if it has.</param>
/// <param name="Outcome">Running, Succeeded, Failed or Skipped.</param>
/// <param name="BarsFetched">Rows the source returned.</param>
/// <param name="BarsAccepted">Rows that passed validation.</param>
/// <param name="BarsRejected">Rows validation refused.</param>
/// <param name="BarsStored">Periods not previously held.</param>
/// <param name="BarsRevised">Periods the source restated.</param>
/// <param name="Attempts">How many calls the source needed.</param>
/// <param name="FailureReason">Why it failed or was skipped, when it was.</param>
public sealed record IngestionRunResponse(
    Guid RunId,
    string Source,
    string Interval,
    DateTimeOffset RequestedFromUtc,
    DateTimeOffset RequestedToUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Outcome,
    int BarsFetched,
    int BarsAccepted,
    int BarsRejected,
    int BarsStored,
    int BarsRevised,
    int Attempts,
    string? FailureReason)
{
    /// <summary>Projects an audit record onto the wire contract.</summary>
    /// <param name="run">The run to project.</param>
    /// <returns>The response representation.</returns>
    public static IngestionRunResponse From(IngestionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new IngestionRunResponse(
            run.Id.Value,
            run.Source.Value,
            run.Interval.ToString(),
            run.RequestedFromUtc,
            run.RequestedToUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.Outcome.ToString(),
            run.BarsFetched,
            run.BarsAccepted,
            run.BarsRejected,
            run.BarsStored,
            run.BarsRevised,
            run.Attempts,
            run.FailureReason);
    }
}

/// <summary>
/// The ingestion history of one series.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Interval">The resolution, by name.</param>
/// <param name="Count">How many runs are in this response.</param>
/// <param name="Runs">The runs, newest first.</param>
public sealed record IngestionHistoryResponse(
    Guid InstrumentId,
    string Interval,
    int Count,
    IReadOnlyList<IngestionRunResponse> Runs);

/// <summary>
/// Parses the bar resolution a client asked for.
/// </summary>
/// <remarks>
/// Clients write <c>1d</c> or <c>15m</c>, which is what a chart control and a
/// command bar both use. The enum's names are accepted too, so a response can
/// be fed straight back into a request.
/// </remarks>
public static class BarIntervalParser
{
    /// <summary>The resolution used when a client does not name one.</summary>
    public const string Default = "1d";

    private static readonly Dictionary<string, BarInterval> ByAlias =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = BarInterval.OneMinute,
            ["5m"] = BarInterval.FiveMinutes,
            ["15m"] = BarInterval.FifteenMinutes,
            ["30m"] = BarInterval.ThirtyMinutes,
            ["1h"] = BarInterval.OneHour,
            ["1d"] = BarInterval.OneDay,
            ["eod"] = BarInterval.OneDay,
        };

    /// <summary>
    /// Parses a resolution, falling back to the default when none was given.
    /// </summary>
    /// <param name="value">The client's value, or null.</param>
    /// <param name="interval">The parsed resolution when successful.</param>
    /// <returns><see langword="true"/> when the value names a resolution.</returns>
    public static bool TryParse(string? value, out BarInterval interval)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            interval = ByAlias[Default];
            return true;
        }

        var trimmed = value.Trim();

        if (ByAlias.TryGetValue(trimmed, out interval))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out interval) && interval.IsDeclared();
    }

    /// <summary>Gets the aliases a client may send, for an error message.</summary>
    /// <returns>The accepted aliases, comma separated.</returns>
    public static string DescribeAccepted() =>
        string.Join(", ", ByAlias.Keys.Order(StringComparer.OrdinalIgnoreCase)
            .Select(alias => alias.ToLower(CultureInfo.InvariantCulture)));
}
