using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the instrument endpoints over real HTTP against real PostgreSQL.
/// </summary>
/// <remarks>
/// Exercised through the pipeline rather than by calling the handlers, so
/// model binding, status codes, problem responses and serialisation are all
/// covered by the same test that covers the behaviour.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class InstrumentEndpointTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("/instruments/search")]
    [InlineData("/instruments/search?q=")]
    [InlineData("/instruments/search?q=%20%20")]
    public async Task A_blank_query_is_a_client_error(string path)
    {
        // "You did not ask me anything" and "nothing matches what you asked"
        // are different situations, and a caller that cannot tell them apart
        // shows the user the wrong message.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(InstrumentSearchCriteria.MaxLimit + 1)]
    public async Task A_limit_outside_the_permitted_range_is_a_client_error(int limit)
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri($"/instruments/search?q=API&limit={limit}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_search_returns_ranked_results_and_echoes_the_folded_query()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("APIRA");
        await host.AddInstrumentsAsync(
            venue,
            ("APQ", "APQ Corporation"),
            ("APR", "APQ Holdings Joint Stock Company"));

        // Act
        var payload = await GetAsync<InstrumentSearchResponse>(host, "/instruments/search?q=+apq+");

        // Assert
        Assert.Equal("APQ", payload.Query);
        Assert.Equal(2, payload.Count);
        Assert.Equal(InstrumentSearchCriteria.DefaultLimit, payload.Limit);
        Assert.Equal("APQ", payload.Results[0].Ticker);
        Assert.Equal(nameof(InstrumentMatchKind.ExactTicker), payload.Results[0].MatchKind);
        Assert.Equal(nameof(InstrumentMatchKind.NamePrefix), payload.Results[1].MatchKind);
    }

    [Fact]
    public async Task A_search_result_carries_everything_needed_to_identify_the_security()
    {
        // Symbol, name, asset class, venue and currency are what the user
        // reads; the identifier is what every module downstream joins on.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("APIRB");
        await host.AddInstrumentsAsync(venue, ("APS", "APS Trading Corporation"));

        // Act
        var payload = await GetAsync<InstrumentSearchResponse>(host, "/instruments/search?q=APS");

        // Assert
        var result = Assert.Single(payload.Results);
        Assert.NotEqual(Guid.Empty, result.InstrumentId);
        Assert.Equal("APS", result.Ticker);
        Assert.Equal("APS Trading Corporation", result.Name);
        Assert.Equal(nameof(AssetType.Equity), result.AssetType);
        Assert.Equal("APIRB", result.Exchange);
        Assert.Equal("VND", result.Currency);
        Assert.Equal(nameof(InstrumentStatus.Listed), result.Status);
    }

    [Fact]
    public async Task A_search_that_matches_nothing_is_a_success_with_no_results()
    {
        // Not a 404. The request was valid and the answer is "none".
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri("/instruments/search?q=NOSUCHTHING", UriKind.Relative),
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(
            Json, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, payload?.Count);
        Assert.Empty(payload?.Results ?? []);
    }

    [Fact]
    public async Task Resolving_a_known_symbol_returns_the_canonical_instrument()
    {
        // Scenario D: the application resolves a symbol without the UI being
        // involved at all.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("APIRC");
        await host.AddInstrumentsAsync(venue, ("APT", "APT Corporation"));

        // Act
        var payload = await GetAsync<InstrumentResolutionResponse>(host, "/instruments/resolve?symbol=apt");

        // Assert
        Assert.Equal(nameof(InstrumentResolutionOutcome.Resolved), payload.Outcome);
        Assert.Equal("APT", payload.Instrument?.Ticker);
        Assert.Equal("APIRC", payload.Instrument?.Exchange);
    }

    [Fact]
    public async Task Resolving_an_unknown_symbol_reports_not_found()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri("/instruments/resolve?symbol=NOPE", UriKind.Relative),
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<InstrumentResolutionResponse>(
            Json, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(nameof(InstrumentResolutionOutcome.NotFound), payload?.Outcome);
    }

    [Fact]
    public async Task A_symbol_live_on_two_venues_resolves_to_a_conflict_carrying_both()
    {
        // Ticker uniqueness is per exchange, so the database permits this and
        // resolution has to report it rather than pick one. The candidates
        // travel with the response so the caller can disambiguate.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var first = await host.AddExchangeAsync("APIRD");
        var second = await host.AddExchangeAsync("APIRE");

        await host.AddInstrumentsAsync(first, ("APU", "APU Holdings On One Venue"));
        await host.AddInstrumentsAsync(second, ("APU", "APU Holdings On Another Venue"));

        // Act
        var response = await host.Client.GetAsync(
            new Uri("/instruments/resolve?symbol=APU", UriKind.Relative),
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<InstrumentResolutionResponse>(
            Json, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(nameof(InstrumentResolutionOutcome.Ambiguous), payload?.Outcome);
        Assert.Equal(2, payload?.Candidates.Count);
        Assert.Null(payload?.Instrument);
    }

    [Fact]
    public async Task An_exchange_disambiguates_a_shared_symbol()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var first = await host.AddExchangeAsync("APIRF");
        var second = await host.AddExchangeAsync("APIRG");

        await host.AddInstrumentsAsync(first, ("APV", "APV Holdings On One Venue"));
        await host.AddInstrumentsAsync(second, ("APV", "APV Holdings On Another Venue"));

        // Act
        var payload = await GetAsync<InstrumentResolutionResponse>(
            host, "/instruments/resolve?symbol=APV&exchange=APIRG");

        // Assert
        Assert.Equal(nameof(InstrumentResolutionOutcome.Resolved), payload.Outcome);
        Assert.Equal("APIRG", payload.Instrument?.Exchange);
    }

    [Fact]
    public async Task An_invalid_exchange_code_is_a_client_error()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri("/instruments/resolve?symbol=APW&exchange=%2F%2F%2F", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_instrument_can_be_read_back_by_its_canonical_identifier()
    {
        // The trusted path behind a client-side selection: the terminal sends
        // the identifier it holds, and the server re-reads every attribute
        // rather than believing the ticker and name that came with it.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("APIRH");
        await host.AddInstrumentsAsync(venue, ("APX", "APX Corporation"));

        var search = await GetAsync<InstrumentSearchResponse>(host, "/instruments/search?q=APX");
        var id = search.Results[0].InstrumentId;

        // Act
        var payload = await GetAsync<InstrumentResponse>(host, $"/instruments/{id}");

        // Assert
        Assert.Equal(id, payload.InstrumentId);
        Assert.Equal("APX", payload.Ticker);
        // Nothing was ranked, so no match kind is claimed.
        Assert.Null(payload.MatchKind);
    }

    [Fact]
    public async Task An_unknown_identifier_is_a_not_found_problem()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri($"/instruments/{Guid.CreateVersion7()}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_identifier_does_not_match_the_route()
    {
        // The route constrains the segment to a GUID, so nothing reaches the
        // handler to interpret.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        // Act
        var response = await host.Client.GetAsync(
            new Uri("/instruments/not-a-guid", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(ApiHost host, string path)
    {
        var response = await host.Client.GetAsync(
            new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<T>(
            Json, TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        return payload;
    }

    private async Task<ApiHost> CreateHostAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        var host = new ApiHost(factory);

        // Forces host start-up, which runs the migration hosted service.
        _ = await host.Client.GetAsync(
            new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return host;
    }

    /// <summary>
    /// A running API together with a DI scope for arranging data behind it.
    /// </summary>
    private sealed class ApiHost : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IInstrumentRepository _instruments;
        private readonly IExchangeRepository _exchanges;

        public ApiHost(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            Client = factory.CreateClient();
            _scope = factory.Services.CreateAsyncScope();

            _unitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            _instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            _exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        }

        public HttpClient Client { get; }

        public async Task<ExchangeId> AddExchangeAsync(string code)
        {
            var exchange = Exchange.Register(
                ExchangeCode.Create(code), $"Venue {code}", "Asia/Ho_Chi_Minh", Now);

            _exchanges.Add(exchange);
            await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

            return exchange.Id;
        }

        public async Task AddInstrumentsAsync(
            ExchangeId exchangeId,
            params (string Ticker, string Name)[] instruments)
        {
            foreach (var (ticker, name) in instruments)
            {
                var instrument = Instrument.Register(
                    exchangeId,
                    Ticker.Create(ticker),
                    name,
                    AssetType.Equity,
                    CurrencyCode.Vnd,
                    Now);
                instrument.List(Now.AddDays(1));

                _instruments.Add(instrument);
            }

            await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
