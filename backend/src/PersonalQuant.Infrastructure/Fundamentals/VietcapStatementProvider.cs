using System.Globalization;
using System.Text.Json;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Fundamentals;

/// <summary>
/// Vietcap's quarterly statements as vendor values for the IDS reports filed
/// blank from fiscal 2026 (ADR-038).
/// </summary>
/// <remarks>
/// <para>
/// Two reads per ticker, the income statement and the balance sheet, paced
/// at robots.txt's crawl delay. Each quarter becomes one filing named as the
/// consolidated quarterly report, which verification 1 showed it is, and the
/// importer joins it to the IDS report of that quarter.
/// </para>
/// <para>
/// Only the canonical items are copied into rows, coded by Vietcap's own
/// field names. EPS (<c>isa23</c>) is computed, never read. A filing is dated
/// by the fetch, never by Vietcap's <c>publicDate</c>, which a restatement
/// does not move.
/// </para>
/// <para>
/// Quarters from <see cref="FromFiscalYear"/> only, fiscal 2026 by default:
/// earlier quarters are filed with values on IDS, and as vendor filings would
/// only be refused, one finding each (ADR-038 Consequences; QA M1).
/// </para>
/// <para>
/// A run is all or nothing. A refused answer or a response that cannot be read
/// stops it with a <see cref="MarketDataProviderException"/> naming the ticker
/// and section, before anything is joined: no ticker is skipped and no other
/// source stands in (QA M2). Rate limits, server errors, network failures and
/// timeouts are transient. Nothing retries today; a caller that does must keep
/// <see cref="MinimumSpacing"/> between reads, retries included.
/// </para>
/// </remarks>
internal sealed class VietcapStatementProvider(IHttpClientFactory clients, IClock clock, IDelayScheduler delays)
    : IFundamentalsProvider
{
    /// <summary>The source code its lines carry; not the market data adapter's "VCI".</summary>
    public const string ProviderCode = "VCS";

    /// <summary>The named HTTP client it reads through.</summary>
    public const string ClientName = "vietcap-statements";

    /// <summary>The wait between reads: robots.txt's <c>Crawl-delay: 10</c>.</summary>
    public static readonly TimeSpan MinimumSpacing = TimeSpan.FromSeconds(10);

    /// <summary>The first fiscal year read by default: the first filed blank on IDS (ADR-038).</summary>
    public const int FirstFiscalYear = 2026;

    private const string Income = "INCOME_STATEMENT";
    private const string Balance = "BALANCE_SHEET";

    // The canonical items and their labels (ADR-038 §5): revenue, profit to
    // the parent, total assets, equity, and the minority interest within it.
    private static readonly (string Section, string Code, string Label)[] Items =
    [
        (Income, "isa3", "Net sales"),
        (Income, "isa22", "Profit attributable to the parent"),
        (Balance, "bsa53", "Total assets"),
        (Balance, "bsa78", "Owner's equity"),
        (Balance, "bsa210", "Non-controlling interest"),
    ];

    /// <summary>Gets the tickers to read; nothing is read by default.</summary>
    public IReadOnlyList<string> Tickers { get; init; } = [];

    /// <summary>Gets the first fiscal year whose quarters become filings.</summary>
    public int FromFiscalYear { get; init; } = FirstFiscalYear;

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public ValueBasis Basis => ValueBasis.Vendor;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderFiling>> ListAsync(CancellationToken cancellationToken = default)
    {
        var client = clients.CreateClient(ClientName);
        var filings = new List<ProviderFiling>();
        var first = true;

        foreach (var ticker in Tickers.Select(ticker => ticker.Trim().ToUpperInvariant()))
        {
            var bodies = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var section in new[] { Income, Balance })
            {
                if (!first)
                {
                    await delays.DelayAsync(MinimumSpacing, cancellationToken).ConfigureAwait(false);
                }

                first = false;
                var path = $"company/{Uri.EscapeDataString(ticker)}/financial-statement?section={section}";
                using var response = await GetAsync(client, path, ticker, section, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    throw new MarketDataProviderException(
                        $"Vietcap answered {status} for {ticker} {section}; the run stopped and nothing was joined.",
                        isTransient: status == 429 || status >= 500);
                }

                bodies[section] = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            filings.AddRange(Filings(ticker, bodies));
        }

        return filings;
    }

    private IEnumerable<ProviderFiling> Filings(string ticker, Dictionary<string, string> bodies)
    {
        var fetched = clock.UtcNow;
        var quarters = new SortedDictionary<(int Year, int Quarter), Dictionary<string, string>>();
        var rows = new Dictionary<(int Year, int Quarter), List<string>>();

        foreach (var (section, body) in bodies)
        {
            try
            {
                Read(section, body, quarters, rows);
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            {
                throw new MarketDataProviderException(
                    $"Vietcap's {section} for {ticker} cannot be read ({error.Message}); the run stopped and nothing was joined.",
                    isTransient: false,
                    error);
            }
        }

        foreach (var ((year, quarter), values) in quarters.Where(entry => entry.Key.Year >= FromFiscalYear))
        {
            yield return new ProviderFiling(
                $"{ProviderCode}-{ticker}-{year}Q{quarter}",
                ticker,
                "Báo cáo tài chính Hợp nhất - Quý",
                $"Vietcap quý {quarter} năm {year}",
                DateOnly.FromDateTime(fetched.UtcDateTime),
                fetched,
                year,
                quarter,
                [Table(Income, values), Table(Balance, values)],
                // The quarter's own rows, verbatim: each filing retains what it
                // was read from, not the whole response again per quarter.
                "{" + string.Join(',', rows[(year, quarter)]) + "}",
                "application/json",
                fetched);
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string ticker,
        string section,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException
            || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A cancellation the caller did not ask for is HttpClient's timeout.
            // The kind of failure only, not its message: a DNS or socket
            // error names the host, and the CLI prints this text (PR #32).
            var kind = error is TaskCanceledException ? "timed out" : "connection failed";
            throw new MarketDataProviderException(
                $"Vietcap could not be reached for {ticker} {section} ({kind}); the run stopped and nothing was joined.",
                isTransient: true,
                error);
        }
    }

    private static void Read(
        string section,
        string body,
        SortedDictionary<(int Year, int Quarter), Dictionary<string, string>> quarters,
        Dictionary<(int Year, int Quarter), List<string>> rows)
    {
        using var document = JsonDocument.Parse(body);

        foreach (var row in document.RootElement.GetProperty("data").GetProperty("quarters").EnumerateArray())
        {
            // lengthReport 1-4 is a quarter, 5 the year, which the quarters carry.
            var quarter = row.GetProperty("lengthReport").GetInt32();

            if (quarter is < 1 or > 4)
            {
                continue;
            }

            var key = (row.GetProperty("yearReport").GetInt32(), quarter);

            if (!quarters.TryGetValue(key, out var values))
            {
                quarters[key] = values = new Dictionary<string, string>(StringComparer.Ordinal);
                rows[key] = [];
            }

            rows[key].Add($"\"{section}\":{row.GetRawText()}");

            foreach (var item in Items.Where(item => item.Section == section))
            {
                // Absent or null is no value, never zero.
                if (row.TryGetProperty(item.Code, out var value) && value.ValueKind == JsonValueKind.Number)
                {
                    values[item.Code] = Exact(value);
                }
            }
        }
    }

    private static ProviderStatementTable Table(string section, Dictionary<string, string> values) =>
        new(
            section,
            ["value"],
            [
                .. Items
                    .Where(item => item.Section == section && values.ContainsKey(item.Code))
                    .Select(item => new ProviderStatementRow(item.Label, item.Code, [values[item.Code]])),
            ]);

    /// <summary>The number as Vietcap wrote it, exponent expanded, never round-tripped through a double.</summary>
    private static string Exact(JsonElement value) =>
        decimal.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)
            .ToString("0.############################", CultureInfo.InvariantCulture);
}
