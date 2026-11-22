using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Infrastructure;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Acceptance tests for wiring Vietcap's statements to configuration (sprint 11.6,
/// ADR-038): the provider is registered only when its URL is set, with the tickers
/// configured, and nothing in the repository names a URL by default.
/// </summary>
/// <remarks>
/// The URL and the tickers are invented (ADR-041). <c>example.invalid</c> never
/// resolves, and no test here sends a request.
/// </remarks>
public sealed class VietcapStatementsWiringTests
{
    private const string StatementsUrlKey = "MarketData:VietcapStatementsBaseUrl";
    private const string StatementsTickersKey = "MarketData:VietcapStatementsTickers";
    private const string InventedRoot = "https://example.invalid/statements/api";

    [Fact]
    public void Without_a_statements_url_no_vendor_statement_provider_is_registered()
    {
        var providers = FundamentalsProviders(new Dictionary<string, string?>());

        Assert.DoesNotContain(providers, provider => provider.Code.Value == VietcapStatementProvider.ProviderCode);
    }

    [Fact]
    public void The_market_data_vietcap_url_alone_does_not_register_the_statement_provider()
    {
        // MARKET_DATA_VIETCAP_URL serves adjusted bars. It is a different source,
        // and setting it must not switch vendor statements on.
        var providers = FundamentalsProviders(new Dictionary<string, string?>
        {
            ["MarketData:VietcapBaseUrl"] = "https://example.invalid/bars/",
        });

        Assert.DoesNotContain(providers, provider => provider.Code.Value == VietcapStatementProvider.ProviderCode);
    }

    [Fact]
    public void With_a_statements_url_the_provider_is_registered_with_the_configured_tickers()
    {
        var providers = FundamentalsProviders(new Dictionary<string, string?>
        {
            [StatementsUrlKey] = InventedRoot + "/",
            [StatementsTickersKey] = "AAA,BBB",
        });

        var statements = Assert.Single(providers.OfType<VietcapStatementProvider>());
        Assert.Equal(["AAA", "BBB"], statements.Tickers);
    }

    [Fact]
    public void The_ticker_list_carries_no_blank_entry()
    {
        var providers = FundamentalsProviders(new Dictionary<string, string?>
        {
            [StatementsUrlKey] = InventedRoot + "/",
            [StatementsTickersKey] = " AAA , BBB ,",
        });

        var statements = Assert.Single(providers.OfType<VietcapStatementProvider>());
        Assert.Equal(["AAA", "BBB"], statements.Tickers);
    }

    [Fact]
    public void A_statements_url_without_a_trailing_slash_is_normalised()
    {
        // Relative paths resolve against the base address. Without the slash
        // the last segment would be dropped from every request.
        using var services = Build(new Dictionary<string, string?>
        {
            [StatementsUrlKey] = InventedRoot,
            [StatementsTickersKey] = "AAA",
        });

        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(VietcapStatementProvider.ClientName);

        Assert.Equal(new Uri(InventedRoot + "/"), client.BaseAddress);
    }

    [Fact]
    public void The_options_name_no_default_url_and_no_default_tickers()
    {
        var options = new MarketDataOptions();

        Assert.Equal(string.Empty, Property(options, "VietcapStatementsBaseUrl"));
        Assert.Equal(string.Empty, Property(options, "VietcapStatementsTickers"));
    }

    [Fact]
    public void Compose_maps_both_keys_with_an_empty_default()
    {
        var compose = File.ReadAllText(RepositoryFile("docker-compose.yml"));

        Assert.Matches(
            new Regex(@"MarketData__VietcapStatementsBaseUrl:\s*\$\{MARKET_DATA_VIETCAP_STATEMENTS_URL:-\}\s*$", RegexOptions.Multiline),
            compose);
        Assert.Matches(
            new Regex(@"MarketData__VietcapStatementsTickers:\s*\$\{MARKET_DATA_VIETCAP_STATEMENTS_TICKERS:-\}\s*$", RegexOptions.Multiline),
            compose);
    }

    [Fact]
    public void The_env_example_lists_both_keys_empty()
    {
        var lines = File.ReadAllLines(RepositoryFile(".env.example"));

        Assert.Contains("MARKET_DATA_VIETCAP_STATEMENTS_URL=", lines);
        Assert.Contains("MARKET_DATA_VIETCAP_STATEMENTS_TICKERS=", lines);
        Assert.DoesNotContain(lines, line => Regex.IsMatch(line, @"^MARKET_DATA_VIETCAP_STATEMENTS_\w+=\S"));
    }

    private static List<IFundamentalsProvider> FundamentalsProviders(Dictionary<string, string?> settings)
    {
        using var services = Build(settings);

        return [.. services.GetServices<IFundamentalsProvider>()];
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    private static object? Property(MarketDataOptions options, string name)
    {
        // Read by name: the property is what the wiring PR adds, and a missing
        // one should fail this test, not the build of every other test.
        var property = typeof(MarketDataOptions).GetProperty(name);
        Assert.True(property is not null, $"MarketDataOptions has no {name} property.");

        return property.GetValue(options);
    }

    private static string RepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, name);
    }
}
