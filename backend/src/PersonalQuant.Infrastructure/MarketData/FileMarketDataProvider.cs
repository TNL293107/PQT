using System.Globalization;
using System.Text;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.MarketData;

/// <summary>
/// A market data source backed by CSV files on disk.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation of <see cref="IMarketDataProvider"/>, and a
/// real one rather than a stub. Vietnamese market data is licensed, and a
/// repository that is going to be cloned and run must be able to demonstrate
/// the whole pipeline — fetch, validate, deduplicate, checkpoint, audit —
/// without anyone holding a vendor key. Exports are how most Vietnamese
/// historical data actually changes hands in any case.
/// </para>
/// <para>
/// It is also what keeps the abstraction honest. An interface with one
/// implementation shaped around a single vendor's API is not an abstraction; a
/// file source and an HTTP source having to satisfy the same contract is.
/// </para>
/// <para>
/// Layout is <c>&lt;root&gt;/&lt;interval&gt;/&lt;TICKER&gt;.csv</c>, for
/// example <c>market-data/1d/FPT.csv</c>, with a header row naming the
/// columns. Column order is read from the header rather than assumed, because
/// a silently reordered export is exactly the failure the pipeline's
/// validation exists to catch and it should not be able to originate here.
/// </para>
/// </remarks>
/// <param name="rootDirectory">The directory holding the interval folders.</param>
internal sealed class FileMarketDataProvider(string rootDirectory) : IMarketDataProvider
{
    /// <summary>The code every bar this source produces is recorded under.</summary>
    public const string ProviderCode = "FILE";

    private const string TimestampColumn = "timestamp";
    private const string OpenColumn = "open";
    private const string HighColumn = "high";
    private const string LowColumn = "low";
    private const string CloseColumn = "close";
    private const string VolumeColumn = "volume";
    private const string TurnoverColumn = "turnover";

    private static readonly string[] RequiredColumns =
        [TimestampColumn, OpenColumn, HighColumn, LowColumn, CloseColumn, VolumeColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    /// <remarks>
    /// What a directory of CSV files actually offers: every resolution, no
    /// venue or asset restriction, and no stated coverage floor — a file holds
    /// whatever was exported into it. A vendor declaration looks nothing like
    /// this, and that is the point of declaring it rather than assuming it.
    /// </remarks>
    public ProviderCapability Capability { get; } = new()
    {
        Code = SourceCode.Create(ProviderCode),
        DisplayName = "Local CSV directory",
        Intervals = AllIntervals,

        // Not stated, because a directory's contents are not knowable without
        // reading it. Unknown, never unbounded.
        EarliestAvailable = null,

        ReportedFields = new ProviderReportedFields
        {
            // The column is optional in the format and present in the fixture.
            Turnover = true,

            // A price export carries neither. Both are properties of a
            // corporate-action or restatement feed, and this is neither.
            AnnouncementDates = false,
            Restatements = false,
        },

        Limitations = new ProviderLimitations
        {
            // A local read has no call bound and no spacing to respect.
            MaxPeriodsPerCall = null,
            MinimumCallSpacing = null,

            // A file holds whatever was put in it, and the pipeline's contract
            // is that stored prices are raw. An export of adjusted prices is a
            // different dataset and needs its own source code.
            AdjustsPricesAtSource = false,
        },
    };

    private static IReadOnlySet<BarInterval> AllIntervals { get; } = new HashSet<BarInterval>
    {
        BarInterval.OneMinute,
        BarInterval.FiveMinutes,
        BarInterval.FifteenMinutes,
        BarInterval.ThirtyMinutes,
        BarInterval.OneHour,
        BarInterval.OneDay,
    };

    /// <inheritdoc />
    public async Task<MarketDataFetchResult> FetchBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = ResolvePath(request);

        if (!File.Exists(path))
        {
            // Not transient, and not an empty success. "This source has no
            // data for this instrument" is a real answer that a schedule
            // should see recorded, where a silent zero-bar success would look
            // identical to a market holiday.
            throw new MarketDataProviderException(
                $"No {request.Interval} export exists for {request.Ticker} at this source.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            // A locked or half-written file is worth another attempt; an
            // export being rewritten while a run reads it is ordinary.
            throw new MarketDataProviderException(
                $"The {request.Interval} export for {request.Ticker} could not be read.",
                isTransient: true,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                $"The {request.Interval} export for {request.Ticker} is not readable.",
                isTransient: false,
                exception);
        }

        return Parse(request, lines);
    }

    private string ResolvePath(MarketDataRequest request)
    {
        var fileName = $"{request.Ticker.Value}.csv";
        var candidate = Path.GetFullPath(
            Path.Combine(rootDirectory, ToFolderName(request.Interval), fileName));

        // The ticker is already constrained to ASCII alphanumerics, so it
        // cannot contain a separator or a parent reference. The check is kept
        // anyway: this is the one place in the system where external input
        // reaches a file path, and it should not depend on a rule enforced two
        // layers away.
        var root = Path.GetFullPath(rootDirectory);

        return candidate.StartsWith(root, StringComparison.Ordinal)
            ? candidate
            : throw new MarketDataProviderException(
                $"The resolved path for {request.Ticker} lies outside the market data directory.");
    }

    private static string ToFolderName(BarInterval interval) => interval switch
    {
        BarInterval.OneMinute => "1m",
        BarInterval.FiveMinutes => "5m",
        BarInterval.FifteenMinutes => "15m",
        BarInterval.ThirtyMinutes => "30m",
        BarInterval.OneHour => "1h",
        BarInterval.OneDay => "1d",
        _ => throw new MarketDataProviderException(
            $"'{interval}' is not a resolution this source stores."),
    };

    /// <summary>
    /// Reads the rows covering the request, and returns them with the slice of
    /// the file they came from.
    /// </summary>
    /// <remarks>
    /// The retained payload is the served slice rather than the whole file. A
    /// verbatim response is what matters, and for a file source the response
    /// to a range request <em>is</em> the range; keeping the entire history on
    /// every nightly run would copy the whole export into the raw table each
    /// time.
    /// </remarks>
    private static MarketDataFetchResult Parse(MarketDataRequest request, string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException(
                $"The {request.Interval} export for {request.Ticker} is empty.");
        }

        var header = ReadHeader(request, lines[0]);
        var bars = new List<ProviderBar>();
        var served = new StringBuilder(lines[0]).Append('\n');

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var bar = ReadRow(request, header, line, index + 1);

            // Range filtering happens here because that is what a range
            // request means. Rows outside it are not rejections — nothing
            // asked for them.
            if (!request.Covers(bar.OpenedAtUtc))
            {
                continue;
            }

            bars.Add(bar);
            served.Append(line).Append('\n');
        }

        return new MarketDataFetchResult(served.ToString(), "text/csv", bars);
    }

    private static Dictionary<string, int> ReadHeader(MarketDataRequest request, string line)
    {
        var columns = line.Split(',');
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < columns.Length; index++)
        {
            byName[columns[index].Trim()] = index;
        }

        var missing = RequiredColumns.Where(column => !byName.ContainsKey(column)).ToList();

        return missing.Count == 0
            ? byName
            : throw new MarketDataProviderException(
                $"The {request.Interval} export for {request.Ticker} is missing the column(s): {string.Join(", ", missing)}.");
    }

    private static ProviderBar ReadRow(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string line,
        int lineNumber)
    {
        var fields = line.Split(',');

        var openedAtUtc = ReadTimestamp(request, header, fields, lineNumber);

        return new ProviderBar(
            openedAtUtc,
            ReadDecimal(request, header, fields, OpenColumn, lineNumber),
            ReadDecimal(request, header, fields, HighColumn, lineNumber),
            ReadDecimal(request, header, fields, LowColumn, lineNumber),
            ReadDecimal(request, header, fields, CloseColumn, lineNumber),
            ReadLong(request, header, fields, VolumeColumn, lineNumber),
            ReadOptionalDecimal(request, header, fields, TurnoverColumn, lineNumber));
    }

    private static DateTimeOffset ReadTimestamp(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string[] fields,
        int lineNumber)
    {
        var raw = ReadField(request, header, fields, TimestampColumn, lineNumber);

        // Round-trip and ISO forms only, parsed as UTC. Accepting a
        // locale-dependent format here would make the same file mean different
        // periods on different machines.
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new MarketDataProviderException(
            $"Line {lineNumber} of the {request.Interval} export for {request.Ticker} has an unreadable timestamp '{raw}'.");
    }

    private static decimal ReadDecimal(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber)
    {
        var raw = ReadField(request, header, fields, column, lineNumber);

        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the {request.Interval} export for {request.Ticker} has an unreadable {column} '{raw}'.");
    }

    private static decimal? ReadOptionalDecimal(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber)
    {
        if (!header.TryGetValue(column, out var index) || index >= fields.Length)
        {
            return null;
        }

        var raw = fields[index].Trim();

        if (raw.Length == 0)
        {
            return null;
        }

        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the {request.Interval} export for {request.Ticker} has an unreadable {column} '{raw}'.");
    }

    private static long ReadLong(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber)
    {
        var raw = ReadField(request, header, fields, column, lineNumber);

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the {request.Interval} export for {request.Ticker} has an unreadable {column} '{raw}'.");
    }

    private static string ReadField(
        MarketDataRequest request,
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber)
    {
        if (header.TryGetValue(column, out var index) && index < fields.Length)
        {
            return fields[index].Trim();
        }

        throw new MarketDataProviderException(
            $"Line {lineNumber} of the {request.Interval} export for {request.Ticker} has no {column} column.");
    }
}
