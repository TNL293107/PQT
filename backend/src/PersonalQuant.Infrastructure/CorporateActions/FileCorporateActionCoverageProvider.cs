using System.Globalization;
using System.Text;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.CorporateActions;

/// <summary>
/// A corporate action coverage source backed by a CSV file on disk (ADR-032).
/// </summary>
/// <remarks>
/// <c>ticker,complete_from,swept_on,source</c>, matched by name, dates parsed
/// invariantly. The ticker is the <c>FILE</c> source's own symbol, as in
/// <c>corporate-actions.csv</c>, so <c>VNM.HM</c> resolves. The notices behind
/// each record are listed in <c>data/fixtures/sources/</c>.
/// </remarks>
/// <param name="filePath">The CSV file to read.</param>
internal sealed class FileCorporateActionCoverageProvider(string filePath) : ICorporateActionCoverageProvider
{
    /// <summary>The code records from this source are attributed to.</summary>
    public const string ProviderCode = "FILE";

    private const string TickerColumn = "ticker";
    private const string CompleteFromColumn = "complete_from";
    private const string SweptOnColumn = "swept_on";
    private const string SourceColumn = "source";

    private static readonly string[] RequiredColumns = [TickerColumn, CompleteFromColumn, SweptOnColumn, SourceColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCorporateActionCoverage>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new MarketDataProviderException("No corporate action coverage file exists at the configured path.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new MarketDataProviderException(
                "The corporate action coverage file could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                "The corporate action coverage file is not readable.", isTransient: false, exception);
        }

        return Parse(lines);
    }

    private static List<ProviderCorporateActionCoverage> Parse(string[] lines)
    {
        if (lines.Length == 0)
        {
            throw new MarketDataProviderException("The corporate action coverage file is empty.");
        }

        var header = ReadHeader(lines[0]);
        var rows = new List<ProviderCorporateActionCoverage>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            // The source note may hold commas; it is the last column, so
            // everything after the third comma belongs to it.
            var fields = lines[index].Split(',', header.Count);

            rows.Add(new ProviderCorporateActionCoverage(
                Read(header, fields, TickerColumn) ?? string.Empty,
                ReadDate(header, fields, CompleteFromColumn),
                ReadDate(header, fields, SweptOnColumn),
                Read(header, fields, SourceColumn),
                index + 1));
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

        if (missing.Count > 0)
        {
            throw new MarketDataProviderException(
                $"The corporate action coverage file is missing the column(s): {string.Join(", ", missing)}.");
        }

        return byName[SourceColumn] == columns.Length - 1
            ? byName
            : throw new MarketDataProviderException(
                "The corporate action coverage file must have source as its last column, because a source may hold commas.");
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

    // An unreadable date is left null for the import to refuse with the line
    // number, rather than failing the whole file.
    private static DateOnly? ReadDate(Dictionary<string, int> header, string[] fields, string column) =>
        Read(header, fields, column) is { } raw
        && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
