using System.Globalization;
using System.Net;
using System.Text.Json;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.MarketData;

/// <summary>
/// Daily Vietnamese equity bars from CafeF's public price-history endpoint,
/// serving <strong>raw</strong> prices.
/// </summary>
/// <remarks>
/// <para>
/// The source the schema was designed for. <c>quant.bars</c> holds what the
/// market printed and the adjustment layer multiplies it on read, which is only
/// possible with a feed that has not already done the multiplying. Every other
/// free Vietnamese source checked serves prices adjusted at source; this one
/// publishes the raw close beside the adjusted one and this adapter takes the
/// raw one.
/// </para>
/// <para>
/// The reasoning, the measurements and the corporate action this makes visible
/// are in
/// <c>docs/architecture/decisions/ADR-021-raw-vietnamese-price-history.md</c>.
/// </para>
/// <para>
/// <strong>The date filter fails silently when the format is wrong.</strong>
/// <c>StartDate</c> and <c>EndDate</c> are <c>MM/dd/yyyy</c>; any other format
/// is dropped and the endpoint answers the default question — the most recent
/// sessions — with a well-formed body and <c>Success: true</c>. That is not a
/// hypothetical: it is why this source was recorded as capping at 65 sessions
/// of history and ruled out of Gate A for a fortnight. So the format is written
/// once, in <see cref="QueryDateFormat"/>, and every parsed row is checked
/// against the window that was asked for. A row outside it means the filter did
/// not apply, and that is raised rather than filtered away — quietly dropping
/// them would turn a wrong request into an empty range the run records as
/// covered.
/// </para>
/// <para>
/// Prices arrive in thousands of dong and are scaled here, as decimals. Nothing
/// goes through <c>double</c>: a close that comes back a fraction different
/// compounds into returns the market never produced.
/// </para>
/// <para>
/// The endpoint is undocumented and serves a news site's own tables. It can
/// change without notice, which is what the recorded-response contract tests
/// and the permanent file source exist to survive.
/// </para>
/// </remarks>
/// <param name="clients">Supplies a client per call, so handlers rotate.</param>
internal sealed class CafefMarketDataProvider(IHttpClientFactory clients) : IMarketDataProvider
{
    /// <summary>The code every bar this source produces is recorded under.</summary>
    public const string ProviderCode = "CAFEF";

    /// <summary>The name the configured client is registered under.</summary>
    public const string ClientName = "cafef";

    /// <summary>The path the price history is served from.</summary>
    public const string HistoryPath = "pricehistory.ashx";

    /// <summary>
    /// The format the endpoint reads its date parameters in.
    /// </summary>
    /// <remarks>
    /// American order, on a Vietnamese site, undocumented, and ignored without
    /// complaint when it is wrong. Written down once so no caller can guess.
    /// </remarks>
    public const string QueryDateFormat = "MM/dd/yyyy";

    /// <summary>The date format inside the response body, which is not the query's.</summary>
    public const string BodyDateFormat = "dd/MM/yyyy";

    /// <summary>Rows the endpoint returns per page, whatever <c>PageSize</c> asks for.</summary>
    public const int RowsPerPage = 20;

    /// <summary>
    /// Rows one request may cover in total, however many pages are walked.
    /// </summary>
    /// <remarks>
    /// The real limit, and the one that was mistaken for a limit on history. A
    /// window wider than this returns its most recent rows and drops the rest
    /// without saying so, which is why the declared call bound keeps every
    /// window comfortably underneath it.
    /// </remarks>
    public const int MaxRowsPerRequest = 65;

    /// <summary>Prices are quoted in thousands of dong.</summary>
    private const int PriceScale = 1_000;

    /// <summary>
    /// A bound on paging, so a changed response shape cannot loop forever.
    /// </summary>
    private const int MaxPages = (MaxRowsPerRequest / RowsPerPage) + 2;

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public ProviderCapability Capability { get; } = new()
    {
        Code = SourceCode.Create(ProviderCode),
        DisplayName = "CafeF price history (raw)",

        // Only what has actually been checked. Daily bars for a HOSE equity
        // were verified against the live endpoint from 2006 to 2026; nothing
        // else was, and a declaration is a promise rather than an expectation.
        Intervals = new HashSet<BarInterval> { BarInterval.OneDay },
        Exchanges = new HashSet<ExchangeCode> { ExchangeCode.Create("HOSE") },
        AssetTypes = new HashSet<AssetType> { AssetType.Equity },

        // Data was returned for December 2006 — FPT's listing month — which
        // proves the floor is at least that early and does not establish where
        // it is. Unknown, and no surface may render it as unbounded.
        EarliestAvailable = null,

        ReportedFields = new ProviderReportedFields
        {
            // The response carries GiaTriKhopLenh, and its unit is not
            // documented and not consistent. In 2008 and 2026 it reads as
            // billions of dong; in 2006 the same field is off by roughly a
            // thousand — 137,520 shares at 486,000 dong is 66.8 billion, and
            // the field says 0.07. A turnover wrong by a factor of a thousand
            // for part of the history is worse than an absent one.
            Turnover = false,

            // Both books are published separately and this adapter sums them,
            // so the volume counts everything that traded. The distinction is
            // not cosmetic: matched-only volume understates traded size worst
            // on exactly the days a liquidity filter is deciding something.
            VolumeBasis = VolumeBasis.MatchedAndNegotiated,

            // No corporate action feed, and therefore no announcement dates.
            AnnouncementDates = false,

            // No corrections feed. History is assumed to be rewritten in place,
            // which makes quant.bar_revisions the only record of what changed.
            Restatements = false,
        },

        Limitations = new ProviderLimitations
        {
            // Sixty-five calendar days, against a vendor cap of sixty-five
            // rows. Sessions per calendar day is not a constant, so a bound
            // stated in days has to sit under one stated in sessions with room
            // to spare — this yields about forty-five sessions per call. The
            // checkpoint turns a long backfill into several runs.
            MaxPeriodsPerCall = 65,

            // Spacing is applied by the pipeline's call limiter, which is one
            // policy for every source rather than a per-vendor guess.
            MinimumCallSpacing = null,

            // The fact this whole class exists for.
            AdjustsPricesAtSource = false,
        },
    };

    /// <inheritdoc />
    public async Task<MarketDataFetchResult> FetchBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The window is half-open, and the endpoint's EndDate is inclusive, so
        // the last day asked for is the day before the exclusive end.
        var from = DateOnly.FromDateTime(request.FromUtc.UtcDateTime);
        var lastDay = DateOnly.FromDateTime(request.ToUtc.AddTicks(-1).UtcDateTime);

        var bars = new List<ProviderBar>();
        var pages = new List<string>();

        // A client per call, from the factory. This provider is a singleton —
        // the registry that holds it is one — and a singleton that captured a
        // client would hold its handler, and the DNS that handler resolved, for
        // as long as the process ran.
        using var client = clients.CreateClient(ClientName);

        for (var page = 1; page <= MaxPages; page++)
        {
            var body = await ReadPageAsync(client, request, from, lastDay, page, cancellationToken)
                .ConfigureAwait(false);

            pages.Add(body);

            var rows = Parse(body, request, from, lastDay);

            bars.AddRange(rows);

            // Short page, or none at all: the window is exhausted. The endpoint
            // ignores PageSize and always answers twenty, so anything less is
            // the last page.
            if (rows.Count < RowsPerPage)
            {
                break;
            }
        }

        // Every page verbatim, in order. Not one response, because a window is
        // several — but the whole of what the source said, which is what makes
        // it possible to discard the parsed rows and derive them again when the
        // parsing turns out to have been wrong.
        return new MarketDataFetchResult(
            "[" + string.Join(",", pages) + "]",
            "application/json",
            bars);
    }

    private static async Task<string> ReadPageAsync(
        HttpClient client,
        MarketDataRequest request,
        DateOnly from,
        DateOnly lastDay,
        int page,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{HistoryPath}?Symbol={Uri.EscapeDataString(request.Ticker.Value)}"
                + $"&StartDate={from.ToString(QueryDateFormat, CultureInfo.InvariantCulture)}"
                + $"&EndDate={lastDay.ToString(QueryDateFormat, CultureInfo.InvariantCulture)}"
                + $"&PageIndex={page}&PageSize={RowsPerPage}");

        HttpResponseMessage response;

        try
        {
            response = await client
                .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new MarketDataProviderException(
                "The CafeF price-history endpoint could not be reached.", isTransient: true, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MarketDataProviderException(
                "The CafeF price-history endpoint timed out.", isTransient: true, exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            // A rejected request repeated is a rejected request. Only the
            // statuses describing a busy or broken server are worth retrying.
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.RequestTimeout
                || (int)response.StatusCode >= 500;

            throw new MarketDataProviderException(
                $"The CafeF price-history endpoint answered {(int)response.StatusCode}.",
                transient);
        }
    }

    /// <summary>
    /// Reads one page of rows, and refuses any that fall outside the window.
    /// </summary>
    /// <remarks>
    /// The out-of-window check is the important half. This endpoint answers a
    /// misformatted date by ignoring it, so a historical request that should
    /// return 2016 comes back full of last month — well-formed, successful, and
    /// completely wrong. Dropping those rows would leave an empty result the
    /// pipeline records as a covered range with no data, which is the failure
    /// this whole adapter exists downstream of.
    /// </remarks>
    private static List<ProviderBar> Parse(
        string body,
        MarketDataRequest request,
        DateOnly from,
        DateOnly lastDay)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new MarketDataProviderException(
                "The CafeF price-history response was not JSON. A large response is compressed "
                    + "whether or not the request asked for it; a client without automatic "
                    + "decompression reads the bytes as text and fails here.",
                isTransient: false,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("Success", out var success)
                && success.ValueKind == JsonValueKind.False)
            {
                var message = root.TryGetProperty("Message", out var text)
                    ? text.GetString()
                    : null;

                throw new MarketDataProviderException(
                    $"The CafeF price-history endpoint refused the request: {message ?? "no reason given"}.",
                    isTransient: false);
            }

            if (!root.TryGetProperty("Data", out var envelope)
                || envelope.ValueKind != JsonValueKind.Object
                || !envelope.TryGetProperty("Data", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                throw new MarketDataProviderException(
                    "The CafeF price-history response carried no row array.", isTransient: false);
            }

            var bars = new List<ProviderBar>(rows.GetArrayLength());

            foreach (var row in rows.EnumerateArray())
            {
                var openedAt = ReadDate(row, request);

                if (openedAt < from || openedAt > lastDay)
                {
                    throw new MarketDataProviderException(
                        $"CafeF returned {openedAt:yyyy-MM-dd} for a request covering "
                            + $"{from:yyyy-MM-dd} to {lastDay:yyyy-MM-dd}. The date filter was not "
                            + $"applied — it is read as {QueryDateFormat} and ignored in silence "
                            + "in any other format, so the response describes the most recent "
                            + "sessions instead of the window asked for.",
                        isTransient: false);
                }

                bars.Add(new ProviderBar(
                    new DateTimeOffset(openedAt.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    ReadPrice(row, "GiaMoCua", request),
                    ReadPrice(row, "GiaCaoNhat", request),
                    ReadPrice(row, "GiaThapNhat", request),

                    // The raw close, never GiaDieuChinh. The adjusted column
                    // sits beside it in every row, and taking it would store a
                    // series this system would then adjust a second time.
                    ReadPrice(row, "GiaDongCua", request),
                    ReadVolume(row, "KhoiLuongKhopLenh") + ReadVolume(row, "KLThoaThuan"),

                    // Turnover is published and is not trustworthy across the
                    // whole history. Absent beats wrong by a factor of a
                    // thousand.
                    Turnover: null));
            }

            return bars;
        }
    }

    private static DateOnly ReadDate(JsonElement row, MarketDataRequest request)
    {
        if (!row.TryGetProperty("Ngay", out var value) || value.GetString() is not { } text)
        {
            throw new MarketDataProviderException(
                $"A CafeF row for {request.Ticker} carried no date.", isTransient: false);
        }

        if (!DateOnly.TryParseExact(
                text, BodyDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new MarketDataProviderException(
                $"A CafeF row for {request.Ticker} carried the date '{text}', which is not "
                    + $"{BodyDateFormat}.",
                isTransient: false);
        }

        return date;
    }

    /// <summary>
    /// Reads a price and scales it out of thousands, as a decimal throughout.
    /// </summary>
    private static decimal ReadPrice(JsonElement row, string field, MarketDataRequest request)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new MarketDataProviderException(
                $"A CafeF row for {request.Ticker} carried no {field}.", isTransient: false);
        }

        // GetDecimal, never GetDouble. A binary float cannot hold a decimal
        // price exactly at any width, and the error compounds into returns.
        return value.GetDecimal() * PriceScale;
    }

    /// <summary>
    /// Reads one of the two volume columns, treating an absent one as zero.
    /// </summary>
    /// <remarks>
    /// Zero is right here and is not a guess. A day with no negotiated trades
    /// reports <c>0</c> in that column, which is what most days look like, and
    /// the field is present on every row inspected from 2006 onwards.
    /// </remarks>
    private static long ReadVolume(JsonElement row, string field) =>
        row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            ? (long)value.GetDecimal()
            : 0L;
}
