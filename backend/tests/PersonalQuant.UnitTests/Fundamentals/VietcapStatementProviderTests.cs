using System.Net;
using System.Text;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Infrastructure.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Sprint 11.4 acceptance (ADR-038), written by QA + QC before the code: the
/// Vietcap statement provider reads what verification 1 recorded and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The <em>shape</em> is the one Dev saved from the live endpoint on 2026-10-30
/// (<c>tmp/adr-038-verification/</c>, git-ignored): <c>data.quarters</c> and
/// <c>data.years</c>, one row per period, <c>yearReport</c>, <c>lengthReport</c>
/// 1–4 for a quarter and 5 for a year, <c>publicDate</c>, and Vietcap's own
/// fields <c>isa*</c> / <c>bsa*</c>, numbers written as JSON doubles such as
/// <c>2.3213536857725E13</c>.
/// </para>
/// <para>
/// The <em>numbers</em> are invented. The data policy forbids vendor data in Git
/// (rule 1) and Vietcap's terms forbid copying and distributing it
/// (ADR-029), so these responses are synthetic, like the market data adapter's
/// tests. Nothing here calls the endpoint.
/// </para>
/// <para>
/// Pins, beyond ADR-038's own names: <c>VietcapStatementProvider(IHttpClientFactory,
/// IClock, IDelayScheduler)</c> in <c>PersonalQuant.Infrastructure.Fundamentals</c>,
/// its <c>MinimumSpacing</c> of 10 s (robots.txt <c>Crawl-delay: 10</c>), its <c>ProviderCode</c>
/// "VCS" and <c>ClientName</c>; the provider lists the tickers it is given
/// through <c>Tickers</c>; and each quarter becomes one <see cref="ProviderFiling"/>
/// whose rows carry Vietcap's field name as the code (<c>isa3</c>, ...).
/// </para>
/// </remarks>
public sealed class VietcapStatementProviderTests
{
    // Synthetic figures in the real format: a JSON double with an exponent, as
    // Vietcap writes large values. 1.234567890123E13 = 12,345,678,901,230 exactly.
    private const string Income = """
        {"status":200,"code":0,"data":{
          "years":[{"ticker":"AAA","yearReport":2026,"lengthReport":5,"publicDate":"2027-03-01T00:00:00",
                    "isa3":4.9E13,"isa22":4.0E12,"isa23":1500.0}],
          "quarters":[
            {"ticker":"AAA","yearReport":2026,"lengthReport":1,"publicDate":"2026-04-28T00:00:00",
             "updateDate":"2026-06-01T09:00:00","isa3":1.234567890123E13,"isa22":1.111111111111E12,"isa23":1478.0},
            {"ticker":"AAA","yearReport":2026,"lengthReport":2,"publicDate":"2026-08-22T00:00:00",
             "updateDate":"2026-08-22T09:00:00","isa3":12500000000001.0,"isa22":1200000000000.0,"isa23":1500.0}]}}
        """;

    private const string Balance = """
        {"status":200,"code":0,"data":{
          "years":[],
          "quarters":[
            {"ticker":"AAA","yearReport":2026,"lengthReport":1,"publicDate":"2026-04-28T00:00:00",
             "bsa53":7.3997673121789E13,"bsa78":3.7896654696942E13,"bsa210":1.0E11},
            {"ticker":"AAA","yearReport":2026,"lengthReport":2,"publicDate":"2026-08-22T00:00:00",
             "bsa53":75000000000000.0,"bsa78":38000000000000.0,"bsa210":1.0E11}]}}
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);

    // --- (a) the provider and its filings ------------------------------------

    [Fact]
    public void The_provider_is_vendor_basis_under_its_own_source_code()
    {
        var provider = Provider(new RoutingHandler());

        Assert.Equal(ValueBasis.Vendor, provider.Basis);
        Assert.NotEqual("IDS", provider.Code.Value);
        Assert.NotEqual("VCI", provider.Code.Value); // the market data adapter's code
        Assert.Equal(VietcapStatementProvider.ProviderCode, provider.Code.Value);
    }

    [Fact]
    public async Task Each_quarter_is_one_filing_keyed_by_ticker_year_and_quarter()
    {
        var provider = Provider(new RoutingHandler());

        var filings = await provider.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("AAA", 2026, (int?)1), ("AAA", 2026, (int?)2)],
            filings.Select(filing => (filing.Ticker, filing.FiscalYear, filing.FiscalQuarter)).OrderBy(key => key.FiscalQuarter));
    }

    [Fact]
    public async Task A_year_row_gives_no_filing_and_there_is_no_half_year()
    {
        // lengthReport 5 is the year; Vietcap serves no half-year, so nothing
        // the provider returns may claim a half-year or a whole year.
        var provider = Provider(new RoutingHandler());

        var filings = await provider.ListAsync(TestContext.Current.CancellationToken);

        // Exactly the two quarters: not the year row (lengthReport 5) as a third.
        Assert.Equal(2, filings.Count);
        Assert.All(filings, filing => Assert.InRange(filing.FiscalQuarter ?? 0, 1, 4));
        Assert.DoesNotContain(filings, filing => filing.FiscalQuarter is null);
    }

    [Fact]
    public async Task Every_filing_is_the_consolidated_scope()
    {
        // Verification 1: no scope field, and the values equal IDS's
        // consolidated form, so the provider names the consolidated report.
        var provider = Provider(new RoutingHandler());

        var filings = await provider.ListAsync(TestContext.Current.CancellationToken);

        // Read the way the importer reads every filing: through the classifier.
        Assert.NotEmpty(filings);
        Assert.All(filings, filing =>
        {
            var classified = FilingClassifier.Classify(filing, out var rejection);
            Assert.Null(rejection);
            Assert.Equal(FinancialStatementScope.Consolidated, classified!.Scope);
        });
    }

    // --- (c) dated at the fetch, never at publicDate --------------------------

    [Fact]
    public async Task A_filing_is_dated_by_the_fetch_never_by_vietcaps_public_date()
    {
        var provider = Provider(new RoutingHandler(), clock: new FakeClock(Now));

        var filings = await provider.ListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(filings);
        Assert.All(filings, filing =>
        {
            Assert.Equal(Now, filing.FetchedAtUtc);
            Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime), filing.SubmittedOn);
            Assert.NotEqual(new DateOnly(2026, 4, 28), filing.SubmittedOn);
        });
    }

    // --- (e) and (f) the mapping ----------------------------------------------

    [Fact]
    public async Task The_four_read_items_are_isa3_isa22_bsa53_and_bsa78_and_isa23_is_never_read()
    {
        var provider = Provider(new RoutingHandler());

        var first = (await provider.ListAsync(TestContext.Current.CancellationToken))
            .Single(filing => filing.FiscalQuarter == 1);
        var codes = first.Tables.SelectMany(table => table.Rows).Select(row => row.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Superset(new HashSet<string>(["isa3", "isa22", "bsa53", "bsa78"], StringComparer.Ordinal), codes);
        // EPS is computed from profit and shares (ADR-038), so isa23 never enters a filing.
        Assert.DoesNotContain("isa23", codes);
    }

    [Fact]
    public async Task A_value_written_with_an_exponent_is_read_exactly_as_a_decimal()
    {
        // 1.234567890123E13 is 12,345,678,901,230 exactly; 12500000000001.0 ends
        // in 1, which a double holds but a careless float round-trip would not.
        var provider = Provider(new RoutingHandler());

        var filings = await provider.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12_345_678_901_230m, Value(filings.Single(f => f.FiscalQuarter == 1), "isa3"));
        Assert.Equal(12_500_000_000_001m, Value(filings.Single(f => f.FiscalQuarter == 2), "isa3"));
        Assert.Equal(73_997_673_121_789m, Value(filings.Single(f => f.FiscalQuarter == 1), "bsa53"));
    }

    [Fact]
    public async Task A_field_absent_from_the_response_is_absent_never_zero()
    {
        // Q2's balance sheet has no bsa78 here: it must not appear as a 0 row.
        var balance = Balance.Replace("\"bsa78\":38000000000000.0,", string.Empty, StringComparison.Ordinal);
        var provider = Provider(new RoutingHandler(balance: balance));

        var second = (await provider.ListAsync(TestContext.Current.CancellationToken))
            .Single(filing => filing.FiscalQuarter == 2);

        Assert.DoesNotContain(second.Tables.SelectMany(table => table.Rows), row => row.Code == "bsa78");
    }

    // --- (g) the honest agent ---------------------------------------------------

    [Fact]
    public async Task The_read_names_itself_and_sends_no_referer_or_origin()
    {
        var handler = new RoutingHandler();
        var provider = Provider(handler);

        await provider.ListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("PersonalQuantTerminal/0.1 (+research)", request.UserAgent);
            Assert.Null(request.Referer);
            Assert.False(request.HasOrigin);
        });
    }

    [Fact]
    public void The_registered_client_names_itself_as_the_market_data_adapter_does()
    {
        // The User-Agent is set where the client is registered (ADR-015), so the
        // registration is what a test of the honest agent has to read.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        PersonalQuant.Infrastructure.InfrastructureServiceCollectionExtensions.AddVietcapStatements(
            services, new Uri("https://example.invalid/api/"));

        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);
        var factory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IHttpClientFactory>(provider);
        using var client = factory.CreateClient(VietcapStatementProvider.ClientName);

        Assert.Equal("PersonalQuantTerminal/0.1 (+research)", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.False(client.DefaultRequestHeaders.Contains("Referer"));
        Assert.False(client.DefaultRequestHeaders.Contains("Origin"));
    }

    // --- pacing (robots.txt Crawl-delay: 10) and what is stored -------------------

    [Fact]
    public async Task Consecutive_reads_wait_at_least_ten_seconds_between_them()
    {
        // Two tickers, two sections each: four reads, so three gaps between
        // them. Each gap is a wait of at least 10 s, asserted on the injected
        // scheduler rather than by sleeping.
        var handler = new RoutingHandler();
        var delays = new RecordingDelays();
        var provider = Provider(handler, delays: delays, tickers: ["AAA", "BBB"]);

        await provider.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), VietcapStatementProvider.MinimumSpacing);
        Assert.True(delays.Waits.Count >= handler.Requests.Count - 1, "one wait between each pair of reads");
        Assert.All(delays.Waits, wait => Assert.True(wait >= TimeSpan.FromSeconds(10), $"waited only {wait}"));
    }

    [Fact]
    public async Task A_filing_stores_only_the_canonical_numbers_and_the_raw_response()
    {
        // A response carrying a person's name (an officer field, as vendor
        // company endpoints do) must not have it copied into any row: rows hold
        // the canonical numbers only. The raw response is kept verbatim, as for
        // every provider (ADR-029), and nothing else.
        var withPerson = Income.Replace(
            "\"isa23\":1478.0}",
            "\"isa23\":1478.0,\"ceoName\":\"Nguyen Van A\",\"chairman\":\"Tran Thi B\"}",
            StringComparison.Ordinal);
        var provider = Provider(new RoutingHandler(income: withPerson));

        var first = (await provider.ListAsync(TestContext.Current.CancellationToken))
            .Single(filing => filing.FiscalQuarter == 1);
        var rows = first.Tables.SelectMany(table => table.Rows).ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains(row.Code, new[] { "isa3", "isa22", "bsa53", "bsa78", "bsa210" }));
        Assert.DoesNotContain(rows, row => row.Label.Contains("Nguyen", StringComparison.Ordinal)
            || row.Values.Any(value => value.Contains("Nguyen", StringComparison.Ordinal)));
        // The raw response is retained (the vendor's document, kept as for every
        // provider), so the only copy of the name is inside it.
        Assert.Contains("isa22", first.RawPayload, StringComparison.Ordinal);
    }

    private static decimal Value(ProviderFiling filing, string code)
    {
        var row = filing.Tables.SelectMany(table => table.Rows).Single(row => row.Code == code);
        return decimal.Parse(row.Values[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static VietcapStatementProvider Provider(
        RoutingHandler handler,
        FakeClock? clock = null,
        RecordingDelays? delays = null,
        string[]? tickers = null) =>
        new(new StubClientFactory(handler), clock ?? new FakeClock(Now), delays ?? new RecordingDelays())
        {
            Tickers = tickers ?? ["AAA"],
        };

    /// <summary>Records each wait and returns at once, so pacing is asserted without sleeping.</summary>
    private sealed class RecordingDelays : PersonalQuant.Application.Abstractions.IDelayScheduler
    {
        public List<TimeSpan> Waits { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Waits.Add(duration);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : PersonalQuant.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://example.invalid/api/"),
                DefaultRequestHeaders = { { "User-Agent", "PersonalQuantTerminal/0.1 (+research)" } },
            };
    }

    /// <summary>Answers the income or balance section by its query, and records each request.</summary>
    private sealed class RoutingHandler(string income = Income, string balance = Balance) : HttpMessageHandler
    {
        public List<(string UserAgent, string? Referer, bool HasOrigin)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Headers.UserAgent.ToString(),
                request.Headers.Referrer?.ToString(),
                request.Headers.Contains("Origin")));

            var query = request.RequestUri?.Query ?? string.Empty;
            var body = query.Contains("BALANCE_SHEET", StringComparison.Ordinal) ? balance
                : query.Contains("INCOME_STATEMENT", StringComparison.Ordinal) ? income
                : """{"status":200,"code":0,"data":{"years":[],"quarters":[]}}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
