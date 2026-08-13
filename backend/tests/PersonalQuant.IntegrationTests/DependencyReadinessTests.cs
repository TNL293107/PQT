using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Infrastructure.Persistence;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the Phase 0 database and cache requirement end to end: the API
/// connects to a real PostgreSQL, applies its migrations, reaches a real
/// Redis, and reports both through readiness.
/// </summary>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class DependencyReadinessTests(DependencyContainerFixture containers)
{
    [Fact]
    public async Task Readiness_reports_healthy_when_postgres_and_redis_are_reachable()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var report = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(report);
        Assert.Equal("Healthy", report.Status);
        Assert.Equal("Healthy", report.Check("postgres")?.Status);
        Assert.Equal("Healthy", report.Check("redis")?.Status);
    }

    [Fact]
    public async Task Migrations_are_applied_and_the_application_schema_exists()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        // Force the host to build, which runs the migration hosted service.
        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PersonalQuantDbContext>();

        // Act
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        var pending = await dbContext.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(applied);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Database_accepts_a_round_trip_query()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: false);
        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PersonalQuantDbContext>();

        // Act
        var canConnect = await dbContext.Database.CanConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(canConnect);
    }
}
