using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Infrastructure;
using PersonalQuant.Infrastructure.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// QA H on PR #32: the HTTP client factory logs every request URI at
/// Information by default, so a CLI import would print the vendor's URL to the
/// terminal. The statements client logs none of it, at any level.
/// </summary>
/// <remarks>The host is invented (ADR-041), and the stub answers every request: nothing leaves the process.</remarks>
public sealed class VietcapStatementsHttpLoggingTests
{
    private const string InventedHost = "statements.example.invalid";

    [Fact]
    public async Task A_statements_request_logs_no_line_that_names_the_vendor_host()
    {
        var sink = new List<string>();
        var services = new ServiceCollection()
            .AddLogging(logging => logging
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(new CapturingProvider(sink)))
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MarketData:VietcapStatementsBaseUrl"] = $"https://{InventedHost}/api",
                    ["MarketData:VietcapStatementsTickers"] = "AAA",
                })
                .Build());

        var stub = new StubHandler();
        services.Configure<HttpClientFactoryOptions>(
            VietcapStatementProvider.ClientName,
            options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub));

        await using var provider = services.BuildServiceProvider();
        var statements = Assert.Single(provider.GetServices<IFundamentalsProvider>().OfType<VietcapStatementProvider>());

        // The stub's 404 stops the run after its first request, with no wait.
        await Assert.ThrowsAsync<MarketDataProviderException>(() => statements.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, stub.Requests);
        Assert.DoesNotContain(sink, line => line.Contains(InventedHost, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unreachable_vendor_is_reported_without_its_host()
    {
        // A DNS failure's message names the host; the CLI prints the
        // provider's message, so the message must not carry it.
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MarketData:VietcapStatementsBaseUrl"] = $"https://{InventedHost}/api",
                    ["MarketData:VietcapStatementsTickers"] = "AAA",
                })
                .Build());

        var stub = new StubHandler(new HttpRequestException($"No such host is known. ({InventedHost}:443)"));
        services.Configure<HttpClientFactoryOptions>(
            VietcapStatementProvider.ClientName,
            options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub));

        await using var provider = services.BuildServiceProvider();
        var statements = Assert.Single(provider.GetServices<IFundamentalsProvider>().OfType<VietcapStatementProvider>());

        var refused = await Assert.ThrowsAsync<MarketDataProviderException>(() => statements.ListAsync(TestContext.Current.CancellationToken));

        Assert.True(refused.IsTransient);
        Assert.Contains("AAA", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InventedHost, refused.Message, StringComparison.OrdinalIgnoreCase);
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
