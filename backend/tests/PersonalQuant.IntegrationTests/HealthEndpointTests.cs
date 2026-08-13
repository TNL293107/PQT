using System.Net;
using System.Net.Http.Json;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the liveness/readiness split using dependencies that are
/// deliberately unreachable. No container is required, so these run
/// everywhere, including on a machine with no Docker.
/// </summary>
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private PersonalQuantApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = PersonalQuantApiFactory.WithUnreachableDependencies();
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Application_starts_even_though_no_dependency_is_reachable()
    {
        // The host must not refuse to start because PostgreSQL or Redis is
        // down; that is what readiness is for.
        // Act
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_reports_healthy_and_checks_no_external_dependency()
    {
        // Act
        var report = await _client.GetFromJsonAsync<HealthResponse>(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(report);
        Assert.Equal("Healthy", report.Status);

        var self = Assert.Single(report.Checks);
        Assert.Equal("self", self.Name);
        Assert.Equal("Healthy", self.Status);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_dependencies_as_service_unavailable()
    {
        // Act
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_names_postgres_and_redis_individually()
    {
        // The status page shows one row per dependency, so readiness has to
        // report them separately rather than as a single aggregate.
        // Act
        // Read through the response rather than GetFromJsonAsync: readiness is
        // expected to answer 503 here, and the JSON helper would throw on it
        // before the body could be inspected.
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var report = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(report);
        Assert.Equal("Unhealthy", report.Status);
        Assert.Equal(2, report.Checks.Count);
        Assert.Equal("Unhealthy", report.Check("postgres")?.Status);
        Assert.Equal("Unhealthy", report.Check("redis")?.Status);
    }

    [Fact]
    public async Task Readiness_does_not_leak_connection_detail_to_the_caller()
    {
        // A failing health endpoint is a favourite reconnaissance target. It
        // must not disclose hosts, ports, users or driver messages.
        // Act
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("59595", body, StringComparison.Ordinal);
        Assert.DoesNotContain("59596", body, StringComparison.Ordinal);
        Assert.DoesNotContain("quant_user", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_responses_are_not_cacheable()
    {
        // Act
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.NoStore);
    }
}
