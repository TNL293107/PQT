using System.Net;
using System.Text;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the CafeF adapter against recorded response shapes.
/// </summary>
/// <remarks>
/// <para>
/// The <em>shape</em> is recorded; the numbers are invented. The wire format was
/// confirmed against the live endpoint once and written down in ADR-021, and
/// nothing here calls it — a contract test that reaches a third party fails when
/// that party is busy and passes when it is wrong.
/// </para>
/// <para>
/// Synthetic values rather than a captured extract, deliberately. A recognisable
/// vendor extract in this repository is a licensing incident under the data
/// policy, and the parser cannot tell the difference.
/// </para>
/// </remarks>
public sealed class CafefMarketDataProviderTests
{
    private static readonly DateTimeOffset From = new(2016, 5, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2016, 5, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_row_becomes_a_bar_with_prices_scaled_out_of_thousands()
    {
        var provider = Provider(Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 20)));

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        var bar = Assert.Single(result.Bars);

        Assert.Equal(From, bar.OpenedAtUtc);
        Assert.Equal(47_900m, bar.Open);
        Assert.Equal(48_000m, bar.High);
        Assert.Equal(47_700m, bar.Low);
        Assert.Equal(47_700m, bar.Close);
    }

    [Fact]
    public async Task The_raw_close_is_taken_and_the_adjusted_one_beside_it_is_not()
    {
        // The single most important line in this adapter. Both columns are in
        // every row; taking the adjusted one would store a series this system
        // then adjusts a second time, and the result stays plausible while being
        // wrong by the product of every factor since.
        var provider = Provider(Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0)));

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(47_700m, Assert.Single(result.Bars).Close);
    }

    [Fact]
    public async Task Both_books_are_counted_in_the_volume()
    {
        // Matched orders plus negotiated blocks, which is what the capability
        // declares. Counting only the first understates traded size worst on
        // exactly the days a liquidity filter is deciding something.
        var provider = Provider(Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 4_098_200, 1_081_000)));

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(5_179_200, Assert.Single(result.Bars).Volume);
    }

    [Fact]
    public async Task Turnover_is_absent_because_its_unit_is_not_consistent()
    {
        // Published, and not trustworthy across the whole history: billions of
        // dong in 2008 and 2026, off by a thousand in 2006. Absent beats wrong.
        var provider = Provider(Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0)));

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Bars).Turnover);
    }

    [Fact]
    public async Task A_fractional_price_survives_exactly()
    {
        // Decimals from the response text, never through double. A binary float
        // cannot hold a decimal price exactly at any width.
        var provider = Provider(Page(Row("24/05/2016", 47.55, 47.55, 47.55, 47.55, 8.53, 100, 0)));

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(47_550m, Assert.Single(result.Bars).Close);
    }

    [Fact]
    public async Task A_row_outside_the_requested_window_is_refused_rather_than_dropped()
    {
        // The defect this adapter is downstream of. A misformatted date is not
        // rejected by the endpoint — it is dropped, and the response describes
        // the most recent sessions instead. Filtering those rows away would
        // leave an empty result the pipeline records as a covered range with no
        // data, which is indistinguishable from a market that was closed.
        var provider = Provider(Page(Row("03/09/2026", 72.2, 73, 72, 72.2, 72.2, 100, 0)));

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains("2026-09-03", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MM/dd/yyyy", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task The_request_states_its_dates_in_the_only_format_the_endpoint_reads()
    {
        // Asserted on the wire, because getting this wrong produces a
        // successful-looking answer to a different question.
        var handler = new StubHandler(Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0)));
        var provider = new CafefMarketDataProvider(new StubClientFactory(handler));

        await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Contains("StartDate=05/24/2016", handler.LastPath, StringComparison.Ordinal);

        // The window is half-open and the endpoint's EndDate is inclusive, so
        // the last day asked for is the day before the exclusive end.
        Assert.Contains("EndDate=05/27/2016", handler.LastPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_page_is_followed_by_the_next_one()
    {
        // Twenty rows is a full page whatever PageSize asks for, so a full page
        // means there may be more. Stopping there would silently truncate every
        // window wider than twenty sessions.
        var full = Page([.. Enumerable.Range(0, 20).Select(i => Row(
            $"{(i % 28) + 1:00}/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0))]);

        var handler = new SequenceHandler([full, Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0))]);
        var provider = new CafefMarketDataProvider(new StubClientFactory(handler));

        var request = Request(new DateTimeOffset(2016, 5, 1, 0, 0, 0, TimeSpan.Zero), To);
        var result = await provider.FetchBarsAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(21, result.Bars.Count);
    }

    [Fact]
    public async Task A_short_page_ends_the_walk()
    {
        var handler = new SequenceHandler([Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0))]);
        var provider = new CafefMarketDataProvider(new StubClientFactory(handler));

        await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Every_page_is_retained_verbatim()
    {
        // The payload exists so the parsed rows can be thrown away and derived
        // again when the parsing turns out to have been wrong. A window is
        // several responses, so all of them are kept.
        var first = Page([.. Enumerable.Range(0, 20).Select(i => Row(
            $"{(i % 28) + 1:00}/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0))]);

        var handler = new SequenceHandler([first, Page(Row("24/05/2016", 47.9, 48, 47.7, 47.7, 8.53, 100, 0))]);
        var provider = new CafefMarketDataProvider(new StubClientFactory(handler));

        var request = Request(new DateTimeOffset(2016, 5, 1, 0, 0, 0, TimeSpan.Zero), To);
        var result = await provider.FetchBarsAsync(request, TestContext.Current.CancellationToken);

        Assert.StartsWith("[", result.Payload, StringComparison.Ordinal);
        Assert.EndsWith("]", result.Payload, StringComparison.Ordinal);
        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task A_refusal_in_the_body_is_not_retried()
    {
        // Success: false with a 200. A rejected request repeated is a rejected
        // request, so the pipeline must not spend three attempts on it.
        var provider = Provider(
            """{"Data":null,"Message":"symbol is null or empty","Success":false}""");

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.False(exception.IsTransient);
        Assert.Contains("symbol is null or empty", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Only_a_busy_or_broken_server_is_worth_retrying(
        HttpStatusCode status,
        bool transient)
    {
        var provider = Provider("{}", status);

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(transient, exception.IsTransient);
    }

    [Fact]
    public async Task A_body_that_is_not_json_names_compression_as_the_likely_cause()
    {
        // It has happened on the sibling adapter: a large response arrives
        // compressed unasked, and a client without automatic decompression
        // reads the bytes as text and fails here.
        var provider = Provider(" not json at all");

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains("compressed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_capability_declares_a_raw_source_that_counts_both_books()
    {
        var capability = new CafefMarketDataProvider(
            new StubClientFactory(new StubHandler("{}"))).Capability;

        // The fact the whole adapter exists for, and what makes it the source
        // the schema was designed for.
        Assert.False(capability.Limitations.AdjustsPricesAtSource);

        Assert.Equal(VolumeBasis.MatchedAndNegotiated, capability.ReportedFields.VolumeBasis);
        Assert.False(capability.ReportedFields.Turnover);

        // A stated bound, unlike every source before it — and the reason V10
        // had to start being enforced.
        Assert.Equal(65, capability.Limitations.MaxPeriodsPerCall);

        // Returned data for 2006 proves the floor is at least that early and
        // does not establish where it is.
        Assert.Null(capability.EarliestAvailable);
    }

    private static string Row(
        string date,
        double open,
        double high,
        double low,
        double rawClose,
        double adjustedClose,
        long matchedVolume,
        long negotiatedVolume) =>
        $$"""
        {"Symbol":"AAA","Ngay":"{{date}}","GiaDieuChinh":{{adjustedClose}},
         "GiaDongCua":{{rawClose}},"ThayDoi":"-0,10 (-0,21%)",
         "KhoiLuongKhopLenh":{{matchedVolume}},"GiaTriKhopLenh":1.5,
         "KLThoaThuan":{{negotiatedVolume}},"GtThoaThuan":0.5,
         "GiaMoCua":{{open}},"GiaCaoNhat":{{high}},"GiaThapNhat":{{low}}}
        """;

    private static string Page(params string[] rows) =>
        $$"""
        {"Data":{"TotalCount":{{rows.Length}},"Data":[{{string.Join(",", rows)}}]},
         "Message":null,"Success":true}
        """;

    private static CafefMarketDataProvider Provider(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubClientFactory(new StubHandler(body, status)));

    private static MarketDataRequest Request(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        Assert.True(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("AAA"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            from ?? From,
            to ?? To,
            out var request,
            out var problem), problem);

        return request;
    }

    /// <summary>Hands out clients over one handler.</summary>
    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            // disposeHandler: false, matching the real factory — a client is
            // disposable, the handler behind it is pooled and outlives it.
            new(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://example.invalid/history/"),
            };
    }

    /// <summary>Answers every request with one body, and records the path.</summary>
    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string LastPath { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastPath = Uri.UnescapeDataString(request.RequestUri?.ToString() ?? string.Empty);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Answers each request with the next body in a list.</summary>
    private sealed class SequenceHandler(IReadOnlyList<string> bodies) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = CallCount < bodies.Count ? bodies[CallCount] : """{"Data":{"Data":[]}}""";

            CallCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
