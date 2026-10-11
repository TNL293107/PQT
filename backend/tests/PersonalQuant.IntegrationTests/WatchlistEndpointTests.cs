using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
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
/// Verifies the watchlist endpoints — the first writes — and the operator key
/// in front of them (ADR-027), over real HTTP against real PostgreSQL.
/// </summary>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class WatchlistEndpointTests(DependencyContainerFixture containers)
{
    private const string Key = "0123456789abcdef0123456789abcdef-watchlist-tests";

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_deployment_without_a_key_refuses_every_write_and_still_reads()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(key: null);
        var token = TestContext.Current.CancellationToken;

        var write = await host.Client.PostAsJsonAsync(
            new Uri("/watchlists", UriKind.Relative), new CreateWatchlistRequest("nokey"), token);
        var read = await host.Client.GetAsync(new Uri("/watchlists", UriKind.Relative), token);

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains("PQT_OPERATOR_KEY", await write.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-key-wrong-key-wrong-key-wrong")]
    [InlineData("Basic " + Key)]
    [InlineData(Key)]
    public async Task A_write_without_the_right_key_is_unauthorised(string? authorization)
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(Key);
        var token = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/watchlists", UriKind.Relative))
        {
            Content = JsonContent.Create(new CreateWatchlistRequest("unauthorised")),
        };

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        var response = await host.Client.SendAsync(request, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());

        // And nothing was written behind the refusal.
        var lookup = await host.Client.GetAsync(new Uri("/watchlists/unauthorised", UriKind.Relative), token);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task A_list_is_created_filled_reordered_by_removal_renamed_and_deleted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(Key);
        var token = TestContext.Current.CancellationToken;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);

        var venue = await host.AddExchangeAsync("WLSTA");
        var first = await host.AddInstrumentAsync(venue, "WLA", "Watch List Alpha");
        var second = await host.AddInstrumentAsync(venue, "WLB", "Watch List Beta");

        // Create.
        var created = await host.Client.PostAsJsonAsync(
            new Uri("/watchlists", UriKind.Relative), new CreateWatchlistRequest("banks-1"), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/watchlists/banks-1", created.Headers.Location?.ToString());

        // Add twice: the second add is the first's answer again.
        await PutAsync(host, $"/watchlists/banks-1/instruments/{first}", HttpStatusCode.OK);
        await PutAsync(host, $"/watchlists/banks-1/instruments/{second}", HttpStatusCode.OK);
        var again = await PutAsync(host, $"/watchlists/banks-1/instruments/{first}", HttpStatusCode.OK);

        Assert.Equal(["WLA", "WLB"], again!.Items.Select(item => item.Ticker));
        Assert.Equal("WLSTA", again.Items[0].ExchangeCode);

        // Remove the first; the second keeps its place, gaps and all.
        var removed = await host.Client.DeleteAsync(
            new Uri($"/watchlists/banks-1/instruments/{first}", UriKind.Relative), token);
        var afterRemoval = await removed.Content.ReadFromJsonAsync<WatchlistResponse>(token);
        Assert.Equal(["WLB"], afterRemoval!.Items.Select(item => item.Ticker));

        // Rename, then read under the new name only.
        var renamed = await host.Client.PatchAsJsonAsync(
            new Uri("/watchlists/banks-1", UriKind.Relative), new RenameWatchlistRequest("banks-2"), token);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.Client.GetAsync(new Uri("/watchlists/banks-1", UriKind.Relative), token)).StatusCode);

        var listed = await host.Client.GetFromJsonAsync<WatchlistListResponse>(
            new Uri("/watchlists", UriKind.Relative), token);
        Assert.Contains(listed!.Results, summary => summary is { Name: "banks-2", Count: 1 });

        // Delete.
        var deleted = await host.Client.DeleteAsync(new Uri("/watchlists/banks-2", UriKind.Relative), token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.Client.GetAsync(new Uri("/watchlists/banks-2", UriKind.Relative), token)).StatusCode);
    }

    [Fact]
    public async Task Refusals_say_which_rule_was_broken()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(Key);
        var token = TestContext.Current.CancellationToken;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);

        var invalid = await host.Client.PostAsJsonAsync(
            new Uri("/watchlists", UriKind.Relative), new CreateWatchlistRequest("Has Spaces"), token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await host.Client.PostAsJsonAsync(
            new Uri("/watchlists", UriKind.Relative), new CreateWatchlistRequest("refusals"), token);
        var taken = await host.Client.PostAsJsonAsync(
            new Uri("/watchlists", UriKind.Relative), new CreateWatchlistRequest("refusals"), token);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        await PutAsync(host, $"/watchlists/refusals/instruments/{Guid.NewGuid()}", HttpStatusCode.NotFound);
        await PutAsync(host, $"/watchlists/no-such-list/instruments/{Guid.NewGuid()}", HttpStatusCode.NotFound);

        // A form post is not JSON, and a write endpoint takes JSON only.
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("name", "form")]);
        var posted = await host.Client.PostAsync(new Uri("/watchlists", UriKind.Relative), form, token);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, posted.StatusCode);
    }

    [Fact]
    public async Task A_workspace_is_saved_replaced_read_and_deleted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(Key);
        var token = TestContext.Current.CancellationToken;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var path = new Uri("/workspaces/morning", UriKind.Relative);

        var created = await host.Client.PutAsJsonAsync(path, new SaveWorkspaceRequest(["breadth VN30"]), token);
        var replaced = await host.Client.PutAsJsonAsync(path, new SaveWorkspaceRequest(["FPT GP", "VCB GP"]), token);
        var read = await host.Client.GetFromJsonAsync<WorkspaceResponse>(path, token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(["FPT GP", "VCB GP"], read!.Entries);

        var empty = await host.Client.PutAsJsonAsync(path, new SaveWorkspaceRequest([]), token);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(path, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path, token)).StatusCode);
    }

    [Fact]
    public async Task No_endpoint_that_writes_is_mapped_without_the_operator_key()
    {
        // The guard is a group convention. This is what stops a later write
        // endpoint, mapped outside the group, from shipping open.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync(Key);

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var writes = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
                && methods.HttpMethods.Any(method => method is not ("GET" or "HEAD" or "OPTIONS")))
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, endpoint => Assert.Contains(
            endpoint.Metadata,
            metadata => metadata.GetType().Name == "OperatorKeyRequirement"));
    }

    private static async Task<WatchlistResponse?> PutAsync(ApiHost host, string path, HttpStatusCode expected)
    {
        var token = TestContext.Current.CancellationToken;
        using var empty = JsonContent.Create(new { });
        var response = await host.Client.PutAsync(new Uri(path, UriKind.Relative), empty, token);

        Assert.Equal(expected, response.StatusCode);

        return expected == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<WatchlistResponse>(token)
            : null;
    }

    private async Task<ApiHost> CreateHostAsync(string? key)
    {
        var factory = PersonalQuantApiFactory.WithSettings(
            containers.Postgres,
            containers.Redis,
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Terminal:OperatorKey"] = key ?? string.Empty });

        var host = new ApiHost(factory);

        // Forces host start-up, which runs the migration hosted service.
        _ = await host.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return host;
    }

    /// <summary>A running API together with a DI scope for arranging data behind it.</summary>
    private sealed class ApiHost : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public ApiHost(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            Client = factory.CreateClient();
            _scope = factory.Services.CreateAsyncScope();
        }

        public HttpClient Client { get; }

        public IServiceProvider Services => _factory.Services;

        public async Task<ExchangeId> AddExchangeAsync(string code)
        {
            var exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            var exchange = Exchange.Register(ExchangeCode.Create(code), $"Venue {code}", "Asia/Ho_Chi_Minh", Now);

            exchanges.Add(exchange);
            await _scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(TestContext.Current.CancellationToken);

            return exchange.Id;
        }

        public async Task<Guid> AddInstrumentAsync(ExchangeId exchangeId, string ticker, string name)
        {
            var instrument = Instrument.Register(
                exchangeId, Ticker.Create(ticker), name, AssetType.Equity, CurrencyCode.Vnd, Now);
            instrument.List(Now.AddDays(1));

            _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().Add(instrument);
            await _scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(TestContext.Current.CancellationToken);

            return instrument.Id.Value;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
