using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure;
using PersonalQuant.Infrastructure.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// QA L1 on PR #32: the HTTP client factory logs every request URI at
/// Information by default, so a bar import would print the vendor's URL
/// wherever it runs, the CLI's terminal included. The Vietcap bars client and
/// the CafeF client log none of it, at any level, as the statements client
/// already does.
/// </summary>
/// <remarks>The hosts are invented (ADR-041), and the stub answers every request: nothing leaves the process.</remarks>
public sealed class MarketDataHttpLoggingTests
{
    private const string InventedHost = "bars.example.invalid";

    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Vendors => new() { "Vietcap", "Cafef" };

    [Theory]
    [MemberData(nameof(Vendors))]
    public async Task A_bar_request_logs_no_line_that_names_the_vendor_host(string vendor)
    {
        var sink = new List<string>();
        var stub = new StubHandler();
        await using var provider = Build(vendor, stub, logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new CapturingProvider(sink)));

        // The stub's 404 stops the fetch after its first request.
        await Assert.ThrowsAsync<MarketDataProviderException>(
            () => Resolve(provider, vendor).FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(1, stub.Requests);
        Assert.DoesNotContain(sink, line => line.Contains(InventedHost, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Vendors))]
    public async Task An_unreachable_vendor_is_reported_without_its_host(string vendor)
    {
        // A DNS failure's message names the host; the CLI prints the
        // provider's message, so the message must not carry it. The inner
        // exception still does, which is why Debug output stays private.
        var stub = new StubHandler(new HttpRequestException($"No such host is known. ({InventedHost}:443)"));
        await using var provider = Build(vendor, stub, _ => { });

        var refused = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => Resolve(provider, vendor).FetchBarsAsync(Request(), TestContext.Current.CancellationToken));

        Assert.True(refused.IsTransient);
        Assert.DoesNotContain(InventedHost, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider Build(string vendor, HttpMessageHandler stub, Action<ILoggingBuilder> logging)
    {
        var services = new ServiceCollection()
            .AddLogging(logging)
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"MarketData:{vendor}BaseUrl"] = $"https://{InventedHost}/api",
                })
                .Build());

        var clientName = vendor == "Vietcap" ? VietcapMarketDataProvider.ClientName : CafefMarketDataProvider.ClientName;
        services.Configure<HttpClientFactoryOptions>(
            clientName,
            options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub));

        return services.BuildServiceProvider();
    }

    private static IMarketDataProvider Resolve(IServiceProvider provider, string vendor)
    {
        var providers = provider.GetServices<IMarketDataProvider>();
        return vendor == "Vietcap"
            ? Assert.Single(providers.OfType<VietcapMarketDataProvider>())
            : Assert.Single(providers.OfType<CafefMarketDataProvider>());
    }

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

    private sealed class StubHandler(Exception? failure = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return failure is null
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                : Task.FromException<HttpResponseMessage>(failure);
        }
    }

    private sealed class CapturingProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string category, List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink)
            {
                sink.Add($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
