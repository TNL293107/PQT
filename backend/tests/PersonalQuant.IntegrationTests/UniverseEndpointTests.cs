using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Reads universes and their breadth over HTTP, from real PostgreSQL, through
/// the real composition root.
/// </summary>
/// <remarks>
/// The unit tests prove the definition over in-memory reads. This proves the
/// bulk reads behind it: that the member set, both sessions' bars and the
/// factor going ex between them come back from the database in the shapes the
/// definition assumes — a daily bar at midnight UTC of its session, a factor
/// keyed by its ex-date — and that an ex-dividend gap survives the trip as
/// unchanged rather than as a decline.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class UniverseEndpointTests(DependencyContainerFixture containers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The instant the host's clock is fixed at: Saturday 3 October.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly CoverageFrom = new(2026, 9, 1);
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly DateOnly Wednesday = new(2026, 9, 30);

    [Fact]
    public async Task The_listing_states_each_universe_s_coverage_and_today_s_members()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var arranged = await ArrangeAsync(host);

        var listing = await host.Client.GetFromJsonAsync<UniverseListResponse>(
            new Uri("/universes", UriKind.Relative), Json, TestContext.Current.CancellationToken);

        Assert.NotNull(listing);
        Assert.Equal(new DateOnly(2026, 10, 3), listing.AsOf);

        var universe = Assert.Single(listing.Universes, entry => entry.Code == arranged.Code);
        Assert.Equal("Index", universe.Kind);
        Assert.Equal(CoverageFrom, universe.CoverageFrom);
        Assert.Null(universe.CoverageUntil);
        Assert.Equal(3, universe.MemberCount);
    }

    [Fact]
    public async Task Breadth_counts_an_ex_dividend_gap_as_unchanged_and_a_missing_bar_as_no_data()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var arranged = await ArrangeAsync(host);

        var breadth = await host.Client.GetFromJsonAsync<BreadthResponse>(
            new Uri($"/universes/{arranged.Code}/breadth?on=2026-09-30", UriKind.Relative),
            Json,
            TestContext.Current.CancellationToken);

        Assert.NotNull(breadth);
        Assert.Equal(Wednesday, breadth.Session);
        Assert.False(breadth.SessionWasDefaulted);
        Assert.Equal("permissive", breadth.AnnouncementPolicy);
        Assert.Null(breadth.KnownAsOf);

        // The leaver went before the session and is not a member that day.
        Assert.Equal(3, breadth.MemberCount);
        Assert.Equal((1, 0, 1, 1), (breadth.Advancing, breadth.Declining, breadth.Unchanged, breadth.NoData));
        Assert.Equal(5_000, breadth.AdvancingVolume);
        Assert.Equal(7_000, breadth.UnchangedVolume);

        var venue = Assert.Single(breadth.Venues);
        Assert.Equal((arranged.Venue, Tuesday), (venue.Code, venue.PreviousSession));

        var payer = Assert.Single(breadth.Members, member => member.Ticker == arranged.Payer);
        Assert.Equal("Unchanged", payer.Category);
        Assert.Equal(25_000m, payer.RawPreviousClose);
        Assert.Equal(24_000m, payer.PreviousClose);
        Assert.Equal(24_000m, payer.Close);
        Assert.Equal(0m, payer.Change);
        Assert.True(payer.AdjustedForAction);

        var riser = Assert.Single(breadth.Members, member => member.Ticker == arranged.Riser);
        Assert.Equal("Advancing", riser.Category);
        Assert.Equal(0.05m, riser.Change);

        var silent = Assert.Single(breadth.Members, member => member.Ticker == arranged.Silent);
        Assert.Equal("NoData", silent.Category);
        Assert.Null(silent.Change);
    }

    [Fact]
    public async Task Without_a_session_the_latest_one_with_member_bars_is_counted_and_said_to_be()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var arranged = await ArrangeAsync(host);

        var breadth = await host.Client.GetFromJsonAsync<BreadthResponse>(
            new Uri($"/universes/{arranged.Code}/breadth", UriKind.Relative),
            Json,
            TestContext.Current.CancellationToken);

        Assert.NotNull(breadth);
        Assert.Equal(Wednesday, breadth.Session);
        Assert.True(breadth.SessionWasDefaulted);
    }

    [Fact]
    public async Task An_unknown_universe_is_not_found()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        var response = await host.Client.GetAsync(
            new Uri("/universes/NO_SUCH_UNIVERSE/breadth?on=2026-09-30", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("UnknownUniverse", await OutcomeAsync(response));
    }

    [Theory]
    [InlineData("/universes/BAD!/breadth")]
    [InlineData("/universes/VN30/breadth?on=2026-13-01")]
    [InlineData("/universes/VN30/breadth?on=30/09/2026")]
    public async Task A_malformed_request_is_a_bad_request(string path)
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        var response = await host.Client.GetAsync(
            new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-10-03", "NotATradingDay")]
    [InlineData("2026-09-02", "NotATradingDay")]
    [InlineData("2027-02-01", "CalendarNotCovered")]
    [InlineData("2026-08-31", "MembershipUnknown")]
    public async Task A_session_that_cannot_be_counted_is_refused_with_its_reason(string on, string outcome)
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var arranged = await ArrangeAsync(host);

        var response = await host.Client.GetAsync(
            new Uri($"/universes/{arranged.Code}/breadth?on={on}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(outcome, await OutcomeAsync(response));
    }

    private static async Task<string?> OutcomeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return problem.RootElement.TryGetProperty("outcome", out var outcome) ? outcome.GetString() : null;
    }

    /// <summary>
    /// A venue transcribed for the rest of 2026 with a closure on 2 September,
    /// an index covered from 1 September, and four names: one that rises, one
    /// that goes ex a 1,000 dividend on the session, one with no session bar,
    /// and one that left the week before.
    /// </summary>
    private static async Task<Arranged> ArrangeAsync(Host host)
    {
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var venueCode = $"B{tag}";
        var cancellation = TestContext.Current.CancellationToken;

        await using var scope = host.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var exchanges = services.GetRequiredService<IExchangeRepository>();
        var instruments = services.GetRequiredService<IInstrumentRepository>();
        var universes = services.GetRequiredService<IUniverseRepository>();
        var bars = services.GetRequiredService<IBarRepository>();
        var actions = services.GetRequiredService<ICorporateActionRepository>();

        var venue = Exchange.Register(ExchangeCode.Create(venueCode), "Breadth Test Venue", "Asia/Ho_Chi_Minh", RecordedAt);
        venue.DeclareCalendarCoverage(CalendarCoverage.Create(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)), RecordedAt);
        exchanges.Add(venue);
        exchanges.AddHoliday(TradingHoliday.Record(venue.Id, new DateOnly(2026, 9, 2), "National Day", RecordedAt));

        var code = UniverseCode.Create($"BR_{tag}");
        var universe = Universe.Define(UniverseId.New(), code, "Breadth Test Index", UniverseKind.Index, Source, RecordedAt);
        universe.DeclareCoverage(MembershipCoverage.Create(CoverageFrom, null), Source, RecordedAt);
        universes.Add(universe);
        await unitOfWork.SaveChangesAsync(cancellation);

        var riser = Listed(instruments, venue, $"R{tag}");
        var payer = Listed(instruments, venue, $"P{tag}");
        var silent = Listed(instruments, venue, $"S{tag}");
        var leaver = Listed(instruments, venue, $"L{tag}");
        await unitOfWork.SaveChangesAsync(cancellation);

        foreach (var member in new[] { riser, payer, silent, leaver })
        {
            var spell = UniverseMembership.Admit(universe.Id, member.Id, CoverageFrom, null, Source, RecordedAt);

            if (member == leaver)
            {
                spell.Remove(new DateOnly(2026, 9, 23));
            }

            universes.Add(spell);
        }

        bars.AddRange(
        [
            Bar(riser.Id, Tuesday, 10_000m, 4_000),
            Bar(riser.Id, Wednesday, 10_500m, 5_000),
            Bar(payer.Id, Tuesday, 25_000m, 6_000),
            Bar(payer.Id, Wednesday, 24_000m, 7_000),
            Bar(silent.Id, Tuesday, 8_000m, 1_000),
            Bar(leaver.Id, Tuesday, 9_000m, 1_000),
            Bar(leaver.Id, Wednesday, 8_000m, 1_000),
        ]);

        var dividend = CorporateAction.Record(
            payer.Id, CorporateActionType.CashDividend, Wednesday, ratio: null, cashAmount: 1_000m, Source, RecordedAt);
        actions.Add(dividend);
        actions.AddAdjustment(PriceAdjustment.For(
            dividend, AdjustmentFactor.Create(0.96m, 1m), Price.Create(25_000m), DataRules.AdjustmentVersion, RecordedAt));

        await unitOfWork.SaveChangesAsync(cancellation);

        return new Arranged(code.Value, venueCode, riser.Ticker.Value, payer.Ticker.Value, silent.Ticker.Value);
    }

    private static Instrument Listed(IInstrumentRepository instruments, Exchange venue, string ticker)
    {
        var instrument = Instrument.Register(
            venue.Id, Ticker.Create(ticker), $"{ticker} Test Company", AssetType.Equity, CurrencyCode.Vnd, RecordedAt);
        instrument.List(RecordedAt);
        instruments.Add(instrument);
        return instrument;
    }

    private static OhlcvBar Bar(InstrumentId instrumentId, DateOnly session, decimal close, long volume) =>
        OhlcvBar.Record(
            instrumentId,
            BarInterval.OneDay,
            new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Price.Create(close),
            Price.Create(close),
            Price.Create(close),
            Price.Create(close),
            volume,
            null,
            Source,
            RecordedAt);

    private async Task<Host> CreateHostAsync()
    {
        // The clock is fixed so "the latest session" and "today's members" do
        // not depend on the day the suite happens to run.
        var factory = PersonalQuantApiFactory
            .WithDependencies(containers.Postgres, containers.Redis, applyMigrations: true)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(
                services => services.AddSingleton<IClock>(new FixedClock(Now))));

        var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new Host(factory, client);
    }

    private sealed record Arranged(string Code, string Venue, string Riser, string Payer, string Silent);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class Host(WebApplicationFactory<Program> factory, HttpClient client) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
