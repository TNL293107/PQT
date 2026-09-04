using System.Globalization;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Parses the bar resolution a caller asked for.
/// </summary>
/// <remarks>
/// <para>
/// People write <c>1d</c> or <c>15m</c> — what a chart control, a command bar
/// and a shell prompt all use. The enum's names are accepted too, so a response
/// can be fed straight back into a request.
/// </para>
/// <para>
/// It lives in the application layer rather than in one of the hosts because
/// more than one host asks the question. A second copy in the CLI would drift,
/// and the two interfaces would disagree about what <c>eod</c> means.
/// </para>
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

    /// <summary>
    /// Renders a resolution in the same spelling this parser accepts.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="TryParse"/>, so anything printed can be typed
    /// straight back in. A surface that renders <c>OneDay</c> and accepts
    /// <c>1d</c> makes an operator translate between two vocabularies for no
    /// reason.
    /// </remarks>
    /// <param name="interval">The resolution to render.</param>
    /// <returns>The alias, or the enum's name when the value is not declared.</returns>
    public static string Describe(BarInterval interval) => interval switch
    {
        BarInterval.OneMinute => "1m",
        BarInterval.FiveMinutes => "5m",
        BarInterval.FifteenMinutes => "15m",
        BarInterval.ThirtyMinutes => "30m",
        BarInterval.OneHour => "1h",
        BarInterval.OneDay => "1d",
        _ => interval.ToString(),
    };

    /// <summary>Gets the aliases a client may send, for an error message.</summary>
    /// <returns>The accepted aliases, comma separated.</returns>
    public static string DescribeAccepted() =>
        string.Join(", ", ByAlias.Keys.Order(StringComparer.OrdinalIgnoreCase)
            .Select(alias => alias.ToLower(CultureInfo.InvariantCulture)));
}
