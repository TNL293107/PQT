using System.Globalization;
using System.Text;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.CorporateActions;

/// <summary>
/// A corporate action source backed by a CSV file on disk.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation, for the same reason the other file sources
/// exist: Vietnamese corporate action history is published as exchange
/// disclosures rather than served over an API, and every system that needs it
/// ends up transcribing it into a file.
/// </para>
/// <para>
/// Columns are matched by name. Numbers and dates are parsed invariantly, so
/// the same file means the same actions on every machine — which matters more
/// here than anywhere else in the system, since a ratio read with the wrong
/// decimal separator rescales a decade of prices by a factor of a thousand.
/// </para>
/// </remarks>
/// <param name="filePath">The CSV file to read.</param>
internal sealed class FileCorporateActionProvider(string filePath) : ICorporateActionProvider
{
    /// <summary>The code actions from this source are attributed to.</summary>
    public const string ProviderCode = "FILE";

    private const string SymbolColumn = "symbol";
    private const string TypeColumn = "type";
    private const string ExDateColumn = "ex_date";
    private const string RatioColumn = "ratio";
    private const string CashAmountColumn = "cash_amount";
    private const string RecordDateColumn = "record_date";
    private const string PaymentDateColumn = "payment_date";
    private const string AnnouncedOnColumn = "announced_on";

    private static readonly string[] RequiredColumns = [SymbolColumn, TypeColumn, ExDateColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCorporateAction>> ListActionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new MarketDataProviderException(
                "No corporate action file exists at the configured path.");
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
                "The corporate action file could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The corporate action file is not readable.", isTransient: false, exception);
        }

        return Parse(lines);
    }

    private static List<ProviderCorporateAction> Parse(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The corporate action file is empty.");
        }

        var header = ReadHeader(lines[0]);
        var rows = new List<ProviderCorporateAction>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;

            rows.Add(new ProviderCorporateAction(
                Read(header, fields, SymbolColumn) ?? string.Empty,
                Read(header, fields, TypeColumn) ?? string.Empty,
                ReadDate(header, fields, ExDateColumn, lineNumber, required: true)!.Value,
                ReadDecimal(header, fields, RatioColumn, lineNumber),
                ReadDecimal(header, fields, CashAmountColumn, lineNumber),
                ReadDate(header, fields, RecordDateColumn, lineNumber, required: false),
                ReadDate(header, fields, PaymentDateColumn, lineNumber, required: false),
                ReadDate(header, fields, AnnouncedOnColumn, lineNumber, required: false)));
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
                $"The corporate action file is missing the column(s): {string.Join(", ", missing)}.");
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

    private static decimal? ReadDecimal(
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

        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            // Refused whole rather than skipped. An unreadable ratio is the one
            // field whose absence would silently leave a series unadjusted for
            // an event the file was telling us about.
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the corporate action file has an unreadable {column} '{raw}'.");
    }

    private static DateOnly? ReadDate(
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber,
        bool required)
    {
        var raw = Read(header, fields, column);

        if (raw is null)
        {
            return required
                ? throw new MarketDataProviderException(
                    $"Line {lineNumber} of the corporate action file has no {column}.")
                : null;
        }

        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of the corporate action file has an unreadable {column} '{raw}'.");
    }
}
