using System.Globalization;
using System.Text;
using System.Text.Json;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Fundamentals;

/// <summary>
/// Reads the filings the IDS collector wrote, one JSON document per filing.
/// </summary>
/// <remarks>
/// <para>
/// The SSC's disclosure portal is an Oracle ADF application with no API; its
/// search and its filing pages are driven by the ADF page protocol. Rather
/// than re-implement that protocol, a collector drives the portal in a
/// browser and writes what each filing page shows, unparsed: the listing's
/// columns, the form's period, and each statement tab's headings and cells.
/// This provider reads those documents. The two-stage shape is ADR-015's.
/// </para>
/// <para>
/// Each document is the filing's raw payload and is retained verbatim. Its
/// values are cell text, so the provider stops at the wire: nothing here
/// parses a number or decides a period.
/// </para>
/// </remarks>
/// <param name="directory">The directory the collector writes to, searched recursively.</param>
internal sealed class IdsFilingFileProvider(string directory) : IFundamentalsProvider
{
    /// <summary>The source every filing is stamped with.</summary>
    public const string ProviderCode = "IDS";

    /// <summary>The document schema this provider reads.</summary>
    public const string Schema = "pqt.ids-filing/1";

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    /// <remarks>The values are the issuer's own form on the regulator's portal.</remarks>
    public ValueBasis Basis => ValueBasis.AsFiled;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderFiling>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            throw new MarketDataProviderException("No IDS filings directory exists at the configured path.");
        }

        var filings = new List<ProviderFiling>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string payload;

            try
            {
                payload = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new MarketDataProviderException(
                    $"The IDS filing {Path.GetFileName(path)} could not be read.", isTransient: true, exception);
            }

            filings.Add(Parse(payload, Path.GetFileName(path)));
        }

        return filings;
    }

    /// <summary>Reads one collector document.</summary>
    /// <param name="payload">The document.</param>
    /// <param name="name">Its file name, for messages.</param>
    /// <returns>The filing.</returns>
    /// <exception cref="MarketDataProviderException">The document is not one this provider reads.</exception>
    internal static ProviderFiling Parse(string payload, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (Text(root, "schema") != Schema)
            {
                throw new MarketDataProviderException($"{name} is not a '{Schema}' document.");
            }

            if (Text(root, "source") != ProviderCode)
            {
                throw new MarketDataProviderException($"{name} does not come from {ProviderCode}.");
            }

            var tables = root.GetProperty("tables").EnumerateArray()
                .Select(table => new ProviderStatementTable(
                    Text(table, "name"),
                    [.. table.GetProperty("columns").EnumerateArray().Select(column => column.GetString() ?? string.Empty)],
                    [.. table.GetProperty("rows").EnumerateArray().Select(row => new ProviderStatementRow(
                        Text(row, "label"),
                        Text(row, "code"),
                        [.. row.GetProperty("values").EnumerateArray().Select(value => value.GetString() ?? string.Empty)]))]))
                .ToList();

            return new ProviderFiling(
                Text(root, "reference"),
                Text(root, "ticker"),
                Text(root, "reportName"),
                OptionalText(root, "summary") ?? string.Empty,
                DateOnly.ParseExact(Text(root, "submittedOn"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                OptionalText(root, "submittedAtUtc") is { } at
                    ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                    : null,
                root.GetProperty("fiscalYear").GetInt32(),
                root.TryGetProperty("fiscalQuarter", out var quarter) && quarter.ValueKind == JsonValueKind.Number
                    ? quarter.GetInt32()
                    : null,
                tables,
                payload,
                "application/json",
                DateTimeOffset.Parse(Text(root, "fetchedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
                {
                    IndexedOn = OptionalText(root, "indexedOn") is { } indexed
                        ? DateOnly.ParseExact(indexed, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : null,
                };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new MarketDataProviderException($"{name} is not a readable IDS filing: {exception.Message}", isTransient: false, exception);
        }
    }

    private static string Text(JsonElement element, string property) =>
        OptionalText(element, property)
            ?? throw new KeyNotFoundException($"'{property}' is missing or empty.");

    private static string? OptionalText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
