using System.Globalization;
using System.Text;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Exchanges;

/// <summary>
/// A trading calendar source backed by a CSV file on disk.
/// </summary>
/// <remarks>
/// <para>
/// A file rather than an API, and that is not a compromise. Vietnam's exchange
/// calendar is published once a year as a decree, not served over HTTP, and
/// every system that needs it ends up transcribing it into a file. Making that
/// file the source of record is honest about where the data actually comes
/// from.
/// </para>
/// <para>
/// Columns are matched by name. Dates are ISO and parsed invariantly, so the
/// same file means the same closures on every machine.
/// </para>
/// </remarks>
/// <param name="filePath">The CSV calendar to read.</param>
/// <param name="coveragePath">
/// The CSV declaring how far the calendar was transcribed, or an empty string
/// when nothing declares it.
/// </param>
internal sealed class FileTradingCalendarProvider(string filePath, string coveragePath = "")
    : ITradingCalendarProvider
{
    /// <summary>The code this source is known by.</summary>
    public const string ProviderCode = "FILE";

    private const string ExchangeColumn = "exchange";
    private const string DateColumn = "date";
    private const string NameColumn = "name";
    private const string FromColumn = "coverage_from";
    private const string UntilColumn = "coverage_until";

    private static readonly string[] RequiredColumns = [ExchangeColumn, DateColumn, NameColumn];

    private static readonly string[] RequiredCoverageColumns = [ExchangeColumn, FromColumn, UntilColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderTradingHoliday>> ListHolidaysAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new MarketDataProviderException(
                "No trading calendar exists at the configured path.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new MarketDataProviderException(
                "The trading calendar could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The trading calendar is not readable.", isTransient: false, exception);
        }

        return Parse(lines);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        // A separate file, and separately configured. The closures and the
        // claim about them answer different questions, and a deployment that
        // has transcribed a calendar without recording how far it got is a real
        // state that has to be expressible — it reports unmeasurable rather
        // than guessing, which is exactly what the guess used to get wrong.
        if (string.IsNullOrWhiteSpace(coveragePath))
        {
            return [];
        }

        if (!File.Exists(coveragePath))
        {
            throw new MarketDataProviderException(
                "No trading calendar coverage file exists at the configured path.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(coveragePath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new MarketDataProviderException(
                "The trading calendar coverage file could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The trading calendar coverage file is not readable.", isTransient: false, exception);
        }

        return ParseCoverage(lines);
    }

    /// <summary>
    /// Reads the coverage declarations.
    /// </summary>
    /// <remarks>
    /// An empty <c>coverage_until</c> means the claim runs on. For a Vietnamese
    /// venue that is a claim nobody can make — Tet is lunar and substitute days
    /// are set by annual decree, so the schedule exists only once a notice is
    /// published — but the format does not enforce market structure, and a
    /// venue whose calendar genuinely is open-ended may say so.
    /// </remarks>
    private static List<ProviderCalendarCoverage> ParseCoverage(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The calendar coverage file is empty.");
        }

        var header = ReadHeader(lines[0], RequiredCoverageColumns, "calendar coverage file");
        var rows = new List<ProviderCalendarCoverage>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;
            var code = Read(header, fields, ExchangeColumn);
            var from = Read(header, fields, FromColumn);

            if (code is null || from is null || !TryDate(from, out var parsedFrom))
            {
                // Refused whole rather than skipped. A dropped row leaves a
                // venue with no claim, which reads as "nothing was transcribed"
                // — the opposite of what a file that mentions it is saying.
                throw new MarketDataProviderException(
                    $"Line {lineNumber} of the calendar coverage file does not state a venue and a start date.");
            }

            DateOnly? parsedUntil = null;

            if (Read(header, fields, UntilColumn) is { } until)
            {
                if (!TryDate(until, out var end))
                {
                    throw new MarketDataProviderException(
                        $"Line {lineNumber} of the calendar coverage file has an unreadable end date '{until}'.");
                }

                parsedUntil = end;
            }

            rows.Add(new ProviderCalendarCoverage(code.ToUpperInvariant(), parsedFrom, parsedUntil));
        }

        return rows;
    }

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static List<ProviderTradingHoliday> Parse(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The trading calendar is empty.");
        }

        var header = ReadHeader(lines[0], RequiredColumns);
        var rows = new List<ProviderTradingHoliday>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;

            rows.Add(new ProviderTradingHoliday(
                Read(header, fields, ExchangeColumn) ?? string.Empty,
                ReadDate(header, fields, lineNumber),
                Read(header, fields, NameColumn) ?? string.Empty));
        }

        return rows;
    }

    private static Dictionary<string, int> ReadHeader(
        string line,
        string[] required,
        string what = "trading calendar")
    {
        var columns = line.Split(',');
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < columns.Length; index++)
        {
            byName[columns[index].Trim()] = index;
        }

        var missing = required.Where(column => !byName.ContainsKey(column)).ToList();

        return missing.Count == 0
            ? byName
            : throw new MarketDataProviderException(
                $"The {what} is missing the column(s): {string.Join(", ", missing)}.");
    }

    private static string? Read(Dictionary<string, int> header, string[] fields, string column)
    {
        if (!header.TryGetValue(column, out var index) || index >= fields.Length)
        {
            return null;
        }

        var value = fields[index].Trim();

        return value.Length == 0 ? null : value;
    }

    private static DateOnly ReadDate(
        Dictionary<string, int> header,
        string[] fields,
        int lineNumber)
    {
        var raw = Read(header, fields, DateColumn);

        return raw is not null
            && DateOnly.TryParseExact(
                raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            // A calendar with an unreadable date is refused whole. Skipping the
            // row would move the horizon past a date whose closure was never
            // recorded, and every session in it would then read as missing.
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the trading calendar has an unreadable date '{raw}'.");
    }
}
