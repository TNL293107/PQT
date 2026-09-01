using System.Net;
using System.Text;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the Vietcap adapter against recorded response shapes.
/// </summary>
/// <remarks>
/// <para>
/// The <em>shape</em> is recorded; the numbers are invented. The wire format
/// was confirmed against the live endpoint once and written down in ADR-015,
/// and nothing here calls it — a contract test that reaches a third party fails
/// when that party is busy and passes when it is wrong.
/// </para>
/// <para>
/// Synthetic values rather than a captured extract, deliberately. A recognisable
/// vendor extract in this repository is a licensing incident under the data
/// policy, and the parser cannot tell the difference.
/// </para>
/// </remarks>
public sealed class VietcapMarketDataProviderTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Midnight UTC on 24, 25 and 26 August 2026.</summary>
    private const string Timestamps = "\"1787529600\",\"1787616000\",\"1787702400\"";

    [Fact]
    public async Task A_column_oriented_response_is_read_into_rows()
    {
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[10.5,11.5,12.5],"h":[13.5,13.5,13.5],
              "l":[9.5,9.5,9.5],"c":[11.5,12.5,13.5],"v":[1000,2000,3000],
              "t":[{{Timestamps}}],"accumulatedValue":[1.5,2.5,3.5]}]
            """);

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Bars.Count);
        Assert.Equal(From, result.Bars[0].OpenedAtUtc);
        Assert.Equal(10.5m, result.Bars[0].Open);
        Assert.Equal(13.5m, result.Bars[0].High);
        Assert.Equal(9.5m, result.Bars[0].Low);
        Assert.Equal(11.5m, result.Bars[0].Close);
        Assert.Equal(1000, result.Bars[0].Volume);
    }

    [Fact]
    public async Task A_fractional_price_survives_exactly()
    {
        // The whole reason this adapter parses decimals from the response text.
        // 44810.34 is not representable in binary floating point, and a close
        // that comes back a fraction different compounds into returns the
        // market never produced.
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[44810.34],"h":[44810.34],"l":[44810.34],
              "c":[44810.34],"v":[1000],"t":["1787529600"]}]
            """);

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(44810.34m, Assert.Single(result.Bars).Close);
    }

    [Fact]
    public async Task Turnover_is_absent_even_though_the_response_carries_a_value()
    {
        // accumulatedValue has an undocumented unit. A turnover wrong by a
        // factor of a million is worse than one that is honestly missing, and
        // the capability declares it absent so nothing downstream waits for it.
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[10.5],"h":[10.5],"l":[10.5],"c":[10.5],
              "v":[1000],"t":["1787529600"],"accumulatedValue":[733765.19]}]
            """);

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Bars).Turnover);
    }

    [Fact]
    public async Task Rows_outside_the_requested_window_are_dropped()
    {
        // The endpoint is asked by count, not by range, so it answers with
        // whatever falls before the end instant. The window is half-open: the
        // closing edge belongs to the next request.
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[1,2,3,4,5],"h":[1,2,3,4,5],"l":[1,2,3,4,5],
              "c":[1,2,3,4,5],"v":[1,2,3,4,5],
              "t":["1787443200","1787529600","1787616000","1787702400","1787788800"]}]
            """);

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        // 23 August falls before the window; 27 August is its exclusive end and
        // belongs to the next request. Three sessions remain.
        Assert.Equal(3, result.Bars.Count);
        Assert.Equal(From, result.Bars[0].OpenedAtUtc);
        Assert.Equal(To.AddDays(-1), result.Bars[^1].OpenedAtUtc);
    }

    [Fact]
    public async Task Columns_of_differing_lengths_fail_the_whole_response()
    {
        // Not a row that can be skipped. A column-oriented response aligns by
        // index, so a mismatch means every row after it would pair a price
        // with another period's timestamp.
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[1,2],"h":[1,2],"l":[1,2],"c":[1,2],
              "v":[1],"t":[{{Timestamps}}]}]
            """);

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task A_fractional_volume_is_refused_rather_than_rounded()
    {
        // It would mean the source had begun adjusting volume as well as
        // price, which changes what the series is. Rounding it away would hide
        // that behind a plausible number.
        var provider = Provider($$"""
            [{"symbol":"AAA","o":[1],"h":[1],"l":[1],"c":[1],
              "v":[1000.5],"t":["1787529600"]}]
            """);

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains("1000.5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_for_another_symbol_yields_nothing_rather_than_the_wrong_series()
    {
        var provider = Provider($$"""
            [{"symbol":"ZZZ","o":[1],"h":[1],"l":[1],"c":[1],
              "v":[1],"t":["1787529600"]}]
            """);

        var result = await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Bars);
    }

    [Fact]
    public async Task The_payload_is_returned_verbatim()
    {
        // What makes it possible to throw the parsed rows away and derive them
        // again when the parsing turns out to have been wrong.
        const string Body = """
            [{"symbol":"AAA","o":[1],"h":[1],"l":[1],"c":[1],"v":[1],"t":["1787529600"]}]
            """;

        var result = await Provider(Body)
            .FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(Body, result.Payload);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task A_failure_says_whether_repeating_it_could_help(
        HttpStatusCode status,
        bool transient)
    {
        // A rejected request repeated is a rejected request. Retrying one costs
        // a rate-limit allowance and explains nothing.
        var provider = Provider("{}", status);

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(transient, error.IsTransient);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_permanent_failure()
    {
        var provider = Provider("<html>maintenance</html>");

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.False(error.IsTransient);
    }

    [Fact]
    public void The_source_declares_that_it_adjusts_prices_at_source()
    {
        // The most important line in the adapter. PQT stores what the market
        // printed and applies factors on read; a series from here is already a
        // derived view, and adjusting it again is wrong by the product of every
        // factor since.
        var capability = Provider("[]").Capability;

        Assert.True(capability.Limitations.AdjustsPricesAtSource);
        Assert.False(capability.ReportedFields.Restatements);
        Assert.False(capability.ReportedFields.AnnouncementDates);
    }

    [Fact]
    public void The_source_declares_only_what_has_been_checked()
    {
        // Daily bars for a HOSE equity were verified against the live endpoint.
        // Nothing else was, and an unstated coverage floor is unknown rather
        // than unbounded.
        var capability = Provider("[]").Capability;

        Assert.Equal([BarInterval.OneDay], capability.Intervals);
        Assert.Contains(ExchangeCode.Create("HOSE"), capability.Exchanges);
        Assert.Contains(AssetType.Equity, capability.AssetTypes);
        Assert.Null(capability.EarliestAvailable);
    }

    [Fact]
    public async Task A_client_is_taken_from_the_factory_for_every_call()
    {
        // The provider is a singleton, because the registry that holds it is
        // one. Holding a client would hold its handler — and the DNS that
        // handler resolved — for the life of the process, which is exactly the
        // protection the factory exists to provide.
        var factory = new StubClientFactory(new StubHandler("[]", HttpStatusCode.OK));
        var provider = new VietcapMarketDataProvider(factory);

        await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);
        await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(2, factory.CreatedCount);
        Assert.Equal(VietcapMarketDataProvider.ClientName, factory.LastName);
    }

    [Fact]
    public async Task The_request_names_the_ticker_and_the_end_of_the_window()
    {
        var handler = new StubHandler("[]", HttpStatusCode.OK);
        var provider = new VietcapMarketDataProvider(new StubClientFactory(handler));

        await provider.FetchBarsAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Contains("\"AAA\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("ONE_DAY", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains(
            To.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            handler.LastRequestBody,
            StringComparison.Ordinal);
    }

    private static VietcapMarketDataProvider Provider(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubClientFactory(new StubHandler(body, status)));

    private static MarketDataRequest Request()
    {
        Assert.True(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("AAA"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            From,
            To,
            out var request,
            out var problem), problem);

        return request;
    }

    /// <summary>Hands out clients over one handler, and counts the handing.</summary>
    private sealed class StubClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public int CreatedCount { get; private set; }

        public string? LastName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreatedCount++;
            LastName = name;

            // disposeHandler: false, matching the real factory — a client is
            // disposable, the handler behind it is pooled and outlives it.
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://example.invalid/api/"),
            };
        }
    }

    /// <summary>Answers with a fixed body, and records what it was asked.</summary>
    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
