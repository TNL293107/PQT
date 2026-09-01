using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.MarketData;

/// <summary>
/// Daily Vietnamese equity bars from Vietcap's public charting endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This source serves prices that are already adjusted for corporate
/// actions.</strong> It is declared as such, and the declaration is the most
/// important thing about this class: PQT stores what the market printed and
/// applies factors on read, so a series from here is a <em>different
/// dataset</em> that happens to share a shape. Storing it as raw and adjusting
/// it again produces numbers that stay plausible and are wrong by the product
/// of every factor since.
/// </para>
/// <para>
/// The evidence is in
/// <c>docs/architecture/decisions/ADR-015-vietnam-market-data-provider.md</c>:
/// this endpoint, DNSE and SSI all return <c>44810.34</c> for FPT's close on
/// 11 January 2022, and HOSE trades FPT on a fifty-dong tick. No such price
/// ever traded.
/// </para>
/// <para>
/// Prices are read as <see cref="decimal"/> straight from the response text.
/// Nothing here goes through <c>double</c> — a close that comes back a fraction
/// different compounds into returns the market never produced, and a binary
/// float cannot hold a decimal price exactly at any width.
/// </para>
/// <para>
/// The endpoint is undocumented and serves a broker's own web charts. It can
/// change without notice, which is what the recorded-response contract tests
/// and the permanent file source exist to survive.
/// </para>
/// </remarks>
/// <param name="clients">Supplies a client per call, so handlers rotate.</param>
internal sealed class VietcapMarketDataProvider(IHttpClientFactory clients) : IMarketDataProvider
{
    /// <summary>The code every bar this source produces is recorded under.</summary>
    public const string ProviderCode = "VCI";

    /// <summary>The name the configured client is registered under.</summary>
    public const string ClientName = "vietcap";

    /// <summary>The path the chart data is served from.</summary>
    public const string ChartPath = "chart/OHLCChart/gap-chart";

    private static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public ProviderCapability Capability { get; } = new()
    {
        Code = SourceCode.Create(ProviderCode),
        DisplayName = "Vietcap public chart data (adjusted)",

        // Only what has actually been checked. Daily bars for a HOSE equity
        // were verified against the live endpoint; nothing else was, and a
        // declaration is a promise rather than an expectation.
        Intervals = new HashSet<BarInterval> { BarInterval.OneDay },
        Exchanges = new HashSet<ExchangeCode> { ExchangeCode.Create("HOSE") },
        AssetTypes = new HashSet<AssetType> { AssetType.Equity },

        // Data was returned for January 2022, which proves the floor is at
        // least that early and does not establish where it is. Unknown, and
        // no surface may render it as unbounded.
        EarliestAvailable = null,

        ReportedFields = new ProviderReportedFields
        {
            // The response carries accumulatedValue, whose unit is not
            // documented and appears to be millions of dong. An inferred unit
            // is a turnover wrong by a factor of a million if the inference is
            // wrong, which is worse than an absent one, so it is not mapped.
            Turnover = false,

            // No corporate action feed, and therefore no announcement dates.
            // This is what gates U4's strict mode against this source.
            AnnouncementDates = false,

            // No corrections feed. History is assumed to be rewritten in
            // place, which makes quant.bar_revisions the only record of what
            // changed and when.
            Restatements = false,
        },

        Limitations = new ProviderLimitations
        {
            // Not stated by the source. The request bound already caps a range.
            MaxPeriodsPerCall = null,

            // Spacing is applied by the pipeline's call limiter, which is one
            // policy for every source rather than a per-vendor guess.
            MinimumCallSpacing = null,

            // The fact this whole class exists to declare.
            AdjustsPricesAtSource = true,
        },
    };

    /// <inheritdoc />
    public async Task<MarketDataFetchResult> FetchBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildRequestBody(request);

        HttpResponseMessage response;

        // A client per call, from the factory. This provider is a singleton —
        // the registry that holds it is one — and a singleton that captured a
        // client would hold its handler, and the DNS the handler resolved, for
        // as long as the process ran. The factory rotates handlers; holding one
        // is how that protection is quietly lost.
        using var client = clients.CreateClient(ClientName);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            response = await client
                .PostAsync(new Uri(ChartPath, UriKind.Relative), content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new MarketDataProviderException(
                "The Vietcap chart endpoint could not be reached.", isTransient: true, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MarketDataProviderException(
                "The Vietcap chart endpoint timed out.", isTransient: true, exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A rejected request repeated is a rejected request. Only the
                // statuses that describe a busy or broken server are worth the
                // pipeline's retries.
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500;

                throw new MarketDataProviderException(
                    $"The Vietcap chart endpoint answered {(int)response.StatusCode}.",
                    transient);
            }

            return new MarketDataFetchResult(
                body,
                response.Content.Headers.ContentType?.MediaType ?? "application/json",
                Parse(body, request));
        }
    }

    /// <summary>
    /// Builds the request body the endpoint expects.
    /// </summary>
    /// <remarks>
    /// The endpoint takes an end instant and a count, not a range. The count is
    /// derived from the requested window with a margin, and the rows are
    /// filtered back to the window afterwards — asking for slightly too many is
    /// one call, and asking for too few silently truncates a series.
    /// </remarks>
    private static string BuildRequestBody(MarketDataRequest request)
    {
        var days = (int)Math.Ceiling((request.ToUtc - request.FromUtc).TotalDays);

        // A calendar day is not a session, and a margin costs nothing: the
        // surplus rows fall outside the window and are dropped.
        var countBack = Math.Clamp(days + 10, 1, MarketDataRequest.MaxPeriods);

        return JsonSerializer.Serialize(
            new
            {
                timeFrame = "ONE_DAY",
                symbols = new[] { request.Ticker.Value },
                to = request.ToUtc.ToUnixTimeSeconds(),
                countBack,
            },
            ResponseOptions);
    }

    /// <summary>
    /// Reads the column arrays the endpoint returns into rows.
    /// </summary>
    /// <remarks>
    /// The response is column-oriented — one array per field, aligned by index
    /// — so a length mismatch between them is not a row that can be skipped. It
    /// means the alignment is unknown, and every row after the mismatch would
    /// pair a price with another period's timestamp.
    /// </remarks>
    private static List<ProviderBar> Parse(string body, MarketDataRequest request)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            // Named explicitly because it has happened: the endpoint
            // compresses a large response unasked, and a client without
            // automatic decompression reads the bytes as text and gets this.
            throw new MarketDataProviderException(
                "The Vietcap chart endpoint returned a body that is not JSON. "
                + "If the response was large, check that the client decompresses.",
                isTransient: false,
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new MarketDataProviderException(
                    "The Vietcap chart response was not the expected array of series.");
            }

            var series = FindSeries(document.RootElement, request.Ticker);

            if (series is not { } found)
            {
                // The endpoint answered, and had nothing for this symbol. That
                // is a legitimate answer, not a failure.
                return [];
            }

            var times = ReadArray(found, "t");
            var opens = ReadArray(found, "o");
            var highs = ReadArray(found, "h");
            var lows = ReadArray(found, "l");
            var closes = ReadArray(found, "c");
            var volumes = ReadArray(found, "v");

            var length = times.Length;

            if (opens.Length != length
                || highs.Length != length
                || lows.Length != length
                || closes.Length != length
                || volumes.Length != length)
            {
                throw new MarketDataProviderException(
                    "The Vietcap chart response has columns of differing lengths, "
                    + "so no row in it can be trusted to pair a price with its period.");
            }

            var bars = new List<ProviderBar>(length);

            for (var index = 0; index < length; index++)
            {
                var openedAtUtc = ReadTimestamp(times[index]);

                // The endpoint is asked by count, so it answers with whatever
                // falls before the end instant. The window is half-open, as
                // everywhere else.
                if (openedAtUtc < request.FromUtc || openedAtUtc >= request.ToUtc)
                {
                    continue;
                }

                bars.Add(new ProviderBar(
                    openedAtUtc,
                    ReadPrice(opens[index], "open"),
                    ReadPrice(highs[index], "high"),
                    ReadPrice(lows[index], "low"),
                    ReadPrice(closes[index], "close"),
                    // Matched-order volume. The source reports put-through
                    // trades separately and this column excludes them, so it is
                    // not total traded volume — a distinction that matters to
                    // anything measuring liquidity and is invisible in the
                    // number itself.
                    ReadVolume(volumes[index]),

                    // Deliberately absent. See ReportedFields.Turnover.
                    Turnover: null));
            }

            return bars;
        }
    }

    private static JsonElement? FindSeries(JsonElement root, Ticker ticker)
    {
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (element.TryGetProperty("symbol", out var symbol)
                && symbol.ValueKind == JsonValueKind.String
                && string.Equals(symbol.GetString(), ticker.Value, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    private static JsonElement[] ReadArray(JsonElement series, string property)
    {
        if (!series.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new MarketDataProviderException(
                $"The Vietcap chart response has no '{property}' column.");
        }

        return [.. value.EnumerateArray()];
    }

    /// <summary>
    /// Reads a price as a decimal, never through a binary float.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonElement.GetDecimal"/> parses the number from the response
    /// text directly. Deserialising to <c>double</c> first — which is what a
    /// frame-shaped client library does — loses the exact value before anything
    /// here can see it.
    /// </remarks>
    private static decimal ReadPrice(JsonElement element, string field) =>
        element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,

            JsonValueKind.String when decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,

            _ => throw new MarketDataProviderException(
                $"The Vietcap chart response has an unreadable {field} value."),
        };

    private static long ReadVolume(JsonElement element)
    {
        var value = ReadPrice(element, "volume");

        // Units traded are whole. A fractional one would mean this source has
        // begun adjusting volume as well as price, which changes what the
        // series is and must not be rounded away.
        return decimal.Truncate(value) == value
            ? (long)value
            : throw new MarketDataProviderException(
                $"The Vietcap chart response reports a fractional volume of {value}.");
    }

    private static DateTimeOffset ReadTimestamp(JsonElement element)
    {
        var seconds = element.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,

            JsonValueKind.Number when element.TryGetInt64(out var value) => value,

            _ => throw new MarketDataProviderException(
                "The Vietcap chart response has an unreadable timestamp."),
        };

        // Seconds since the epoch, landing on midnight UTC for a daily bar,
        // which is the opening edge this pipeline stores.
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
