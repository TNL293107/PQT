using System.Globalization;
using System.Text;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Instruments;

/// <summary>
/// A venue history source backed by a CSV file on disk.
/// </summary>
/// <remarks>
/// Like the trading calendar and the universe memberships, venue changes are
/// exchange announcements — a delisting from UPCOM, a first session on HOSE —
/// transcribed into a file with their sources beside them. Columns are matched
/// by name and dates parsed invariantly. Attributed to <c>FILE</c>, the code the
/// instrument list records its aliases under, so <c>BSR.HM</c> resolves.
/// </remarks>
/// <param name="filePath">The CSV file to read.</param>
internal sealed class FileVenueHistoryProvider(string filePath) : IVenueHistoryProvider
{
    /// <summary>The code spells from this source are attributed to.</summary>
    public const string ProviderCode = "FILE";

    private const string SymbolColumn = "symbol";
    private const string ExchangeColumn = "exchange";
    private const string FromColumn = "effective_from";
    private const string ToColumn = "effective_to";

    private static readonly string[] RequiredColumns = [SymbolColumn, ExchangeColumn, FromColumn, ToColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderVenueSpell>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new MarketDataProviderException("No venue history file exists at the configured path.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new MarketDataProviderException(
                "The venue history file could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The venue history file is not readable.", isTransient: false, exception);
        }

        return Parse(lines);
    }

    private static List<ProviderVenueSpell> Parse(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The venue history file is empty.");
        }

        var header = ReadHeader(lines[0]);
        var rows = new List<ProviderVenueSpell>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;

            rows.Add(new ProviderVenueSpell(
                Read(header, fields, SymbolColumn) ?? string.Empty,
                Read(header, fields, ExchangeColumn) ?? string.Empty,
                ReadDate(header, fields, FromColumn, lineNumber),
                ReadDate(header, fields, ToColumn, lineNumber),
                lineNumber));
        }

        return rows;
    }

    private static Dictionary<string, int> ReadHeader(string line)
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
                $"The venue history file is missing the column(s): {string.Join(", ", missing)}.");
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

    private static DateOnly? ReadDate(Dictionary<string, int> header, string[] fields, string column, int lineNumber)
    {
        var raw = Read(header, fields, column);

        if (raw is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the venue history file has an unreadable {column} '{raw}'.");
    }
}
