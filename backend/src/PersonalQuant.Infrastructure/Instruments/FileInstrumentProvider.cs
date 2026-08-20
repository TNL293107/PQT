using System.Globalization;
using System.Text;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Instruments;

/// <summary>
/// An instrument source backed by a CSV symbol list on disk.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation of <see cref="IInstrumentProvider"/>, for the
/// same reasons the file market data source exists: Vietnamese reference data
/// is licensed, and a repository that is going to be cloned and run must be
/// able to demonstrate the whole import pipeline — normalise, deduplicate,
/// alias, reject — without anyone holding a vendor key.
/// </para>
/// <para>
/// Columns are matched by name, not by position. A reordered export is exactly
/// the failure the import's rejections exist to surface, and it should not be
/// able to originate here.
/// </para>
/// </remarks>
/// <param name="filePath">The CSV symbol list to read.</param>
internal sealed class FileInstrumentProvider(string filePath) : IInstrumentProvider
{
    /// <summary>The code aliases from this source are attributed to.</summary>
    public const string ProviderCode = "FILE";

    private const string SymbolColumn = "symbol";
    private const string NameColumn = "name";
    private const string ExchangeColumn = "exchange";
    private const string AssetTypeColumn = "asset_type";
    private const string CurrencyColumn = "currency";
    private const string IsinColumn = "isin";
    private const string FigiColumn = "figi";
    private const string ListedOnColumn = "listed_on";

    private static readonly string[] RequiredColumns = [SymbolColumn, NameColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderInstrument>> ListInstrumentsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new MarketDataProviderException(
                "No instrument symbol list exists at the configured path.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            // A file being rewritten while a run reads it is ordinary and
            // worth another attempt.
            throw new MarketDataProviderException(
                "The instrument symbol list could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The instrument symbol list is not readable.", isTransient: false, exception);
        }

        return Parse(lines);
    }

    private static List<ProviderInstrument> Parse(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The instrument symbol list is empty.");
        }

        var header = ReadHeader(lines[0]);
        var rows = new List<ProviderInstrument>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            rows.Add(ReadRow(header, lines[index], index + 1));
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
                $"The instrument symbol list is missing the column(s): {string.Join(", ", missing)}.");
    }

    private static ProviderInstrument ReadRow(
        Dictionary<string, int> header,
        string line,
        int lineNumber)
    {
        var fields = line.Split(',');

        // Everything but the symbol and the name is optional, because that is
        // what symbol lists look like. A row that is missing them is passed
        // through as it stands and rejected by the import with a reason, so
        // one bad line cannot stop the other four thousand.
        return new ProviderInstrument(
            Read(header, fields, SymbolColumn) ?? string.Empty,
            Read(header, fields, NameColumn) ?? string.Empty,
            Read(header, fields, ExchangeColumn),
            Read(header, fields, AssetTypeColumn),
            Read(header, fields, CurrencyColumn),
            Read(header, fields, IsinColumn),
            Read(header, fields, FigiColumn),
            ReadDate(header, fields, ListedOnColumn, lineNumber));
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

    private static DateOnly? ReadDate(
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber)
    {
        var raw = Read(header, fields, column);

        if (raw is null)
        {
            return null;
        }

        // Invariant ISO format only. A locale-dependent date would make the
        // same file mean different listing dates on different machines, and a
        // listing date is the kind of unsourced value the master refuses to
        // guess at.
        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the instrument symbol list has an unreadable {column} '{raw}'.");
    }
}
