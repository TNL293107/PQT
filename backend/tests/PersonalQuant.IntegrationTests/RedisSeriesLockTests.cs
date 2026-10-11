using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.Caching;
using PersonalQuant.Infrastructure.Configuration;
using StackExchange.Redis;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the ingestion series lock against a real Redis.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline's side of the lock — skip when held, fail closed when the store
/// is down, release however the pass ends — is proved in the unit tests with a
/// fake. What only a real Redis can prove is the part the fake takes on trust:
/// that <c>SET NX</c> excludes a second owner, that the release script deletes
/// only its own owner's key, and that the expiry is actually set.
/// </para>
/// <para>
/// Every test names a fresh instrument, so the tests share one container and
/// never one key.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class RedisSeriesLockTests(DependencyContainerFixture containers)
{
    [Fact]
    public async Task A_second_acquire_is_refused_while_the_series_is_held()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection);
        var instrument = InstrumentId.New();

        // Act
        await using var first = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        var second = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Another_resolution_of_the_same_instrument_is_a_different_series()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection);
        var instrument = InstrumentId.New();

        // Act
        await using var daily = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        await using var hourly = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneHour, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(daily);
        Assert.NotNull(hourly);
    }

    [Fact]
    public async Task Releasing_frees_the_series_for_the_next_pass()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection);
        var instrument = InstrumentId.New();

        var first = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(first);

        // Act
        await first.DisposeAsync();
        await using var next = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(next);
    }

    [Fact]
    public async Task Only_the_owner_token_releases_the_series()
    {
        // A pass that outlived its expiry must not delete the lock a second
        // pass has since taken. Deleting the key by hand stands in for the
        // expiry, so the test does not wait on a clock.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection);
        var instrument = InstrumentId.New();
        var key = RedisSeriesLock.KeyFor(instrument, BarInterval.OneDay);
        var database = (await connection.GetConnectionAsync(TestContext.Current.CancellationToken))
            .GetDatabase();

        var stale = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(stale);

        await database.KeyDeleteAsync(key);

        await using var current = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(current);

        var currentToken = await database.StringGetAsync(key);

        // Act
        await stale.DisposeAsync();

        // Assert
        Assert.Equal(currentToken, await database.StringGetAsync(key));
        Assert.Null(await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_key_carries_the_configured_expiry()
    {
        // A key with no expiry would outlive a crashed pass forever.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection);
        var instrument = InstrumentId.New();
        var database = (await connection.GetConnectionAsync(TestContext.Current.CancellationToken))
            .GetDatabase();

        // Act
        await using var lease = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        var remaining = await database.KeyTimeToLiveAsync(
            RedisSeriesLock.KeyFor(instrument, BarInterval.OneDay));

        // Assert
        Assert.NotNull(lease);
        Assert.NotNull(remaining);
        Assert.InRange(remaining.Value, TimeSpan.FromSeconds(590), TimeSpan.FromSeconds(600));
    }

    [Fact]
    public async Task The_key_expires_after_the_ttl_and_the_series_can_be_taken_again()
    {
        // A process killed mid-pass never releases. The expiry is what gives
        // the series back.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Arrange
        await using var connection = Connect();
        var seriesLock = LockOver(connection, ttlSeconds: 1);
        var instrument = InstrumentId.New();
        var key = RedisSeriesLock.KeyFor(instrument, BarInterval.OneDay);
        var database = (await connection.GetConnectionAsync(TestContext.Current.CancellationToken))
            .GetDatabase();

        // Taken and abandoned: never disposed, as by a process that died.
        var abandoned = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(abandoned);

        // Act
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (await database.KeyExistsAsync(key) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.False(await database.KeyExistsAsync(key));
        await using var retaken = await seriesLock.TryAcquireAsync(
            instrument, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(retaken);
    }

    [Fact]
    public async Task An_unreachable_store_is_reported_rather_than_read_as_free()
    {
        // Needs no container: nothing listens on the discard port.
        // Arrange
        await using var connection = new RedisConnectionProvider(Options.Create(new RedisOptions
        {
            Host = "127.0.0.1",
            Port = 9,
            ConnectTimeoutMilliseconds = 500,
        }));
        var seriesLock = LockOver(connection);

        // Act
        var exception = await Assert.ThrowsAsync<SeriesLockUnavailableException>(
            () => seriesLock.TryAcquireAsync(
                InstrumentId.New(), BarInterval.OneDay, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("Redis", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }

    private RedisConnectionProvider Connect() =>
        new(Options.Create(new RedisOptions
        {
            Host = containers.Redis.Host,
            Port = containers.Redis.Port,
        }));

    private static RedisSeriesLock LockOver(
        IRedisConnectionProvider connection,
        int ttlSeconds = 600) =>
        new(
            connection,
            Options.Create(new MarketDataOptions { IngestionLockTtlSeconds = ttlSeconds }),
            NullLogger<RedisSeriesLock>.Instance);
}
