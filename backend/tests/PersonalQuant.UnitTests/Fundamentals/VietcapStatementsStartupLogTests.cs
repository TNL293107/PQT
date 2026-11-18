using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalQuant.Infrastructure;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Sprint 11.6 step 6: the backend says at startup that Vietcap's statements
/// are registered, and how many tickers it will read, so the operator can check
/// the wiring before an import (plan step 7). It never logs the URL.
/// </summary>
/// <remarks>The URL and the tickers are invented (ADR-041).</remarks>
public sealed class VietcapStatementsStartupLogTests
{
    private const string InventedRoot = "https://example.invalid/statements/api";

    [Fact]
    public async Task A_registered_statement_provider_logs_its_ticker_count_and_not_its_url()
    {
        var logs = await StartAsync(new Dictionary<string, string?>
        {
            ["MarketData:VietcapStatementsBaseUrl"] = InventedRoot,
            ["MarketData:VietcapStatementsTickers"] = "AAA, BBB,CCC",
        });

        var line = Assert.Single(logs, log => log.EventId == 1160);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("3", line.Message, StringComparison.Ordinal);
        Assert.All(logs, log => Assert.DoesNotContain("example.invalid", log.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_statement_provider_means_no_startup_line()
    {
        var logs = await StartAsync(new Dictionary<string, string?>());

        Assert.DoesNotContain(logs, log => log.EventId == 1160);
    }

    private static async Task<IReadOnlyList<Captured>> StartAsync(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var sink = new List<Captured>();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new CapturingProvider(sink)))
            .AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();

        // Only the hosted service under test is started: the others would
        // reach for a database this test has none of.
        foreach (var hosted in provider.GetServices<IHostedService>()
            .Where(service => service.GetType().Name.StartsWith("VietcapStatements", StringComparison.Ordinal)))
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }

        return [.. sink];
    }

    private sealed record Captured(int EventId, LogLevel Level, string Message);

    private sealed class CapturingProvider(List<Captured> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<Captured> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink)
            {
                sink.Add(new Captured(eventId.Id, logLevel, formatter(state, exception)));
            }
        }
    }
}
