using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Diagnostics;
using StackExchange.Redis;

namespace PersonalQuant.Infrastructure.Caching;

/// <summary>
/// Holds an ingestion series in Redis, so that the API host and the operator
/// CLI — separate processes — cannot ingest it at the same time.
/// </summary>
/// <remarks>
/// <para>
/// The single-instance pattern Redis documents: <c>SET key token NX PX ttl</c>
/// to take, and a compare-and-delete script to give back. The token is random
/// per acquisition, so a pass can only ever delete the key it set. Without the
/// comparison, a pass that outlived its expiry would delete the lock a second
/// pass had since taken, and admit a third beside it.
/// </para>
/// <para>
/// Redis rather than PostgreSQL advisory locks, because an advisory lock lives
/// on one connection, and the pipeline's connection is EF Core's, pooled and
/// retried underneath it. A lock that silently disappears when the execution
/// strategy reconnects is not a lock. The key has an expiry instead, so a
/// process that dies holding it costs the series at most that expiry.
/// </para>
/// <para>
/// One Redis, no Redlock. This deployment runs one Redis; the multi-node
/// algorithm guards against a primary failing over with the key unreplicated,
/// which is not a failure this system can have.
/// </para>
/// </remarks>
/// <param name="connectionProvider">Supplies the shared multiplexer.</param>
/// <param name="options">Validated market data settings, for the expiry.</param>
/// <param name="logger">Logger for releases that did not go as planned.</param>
public sealed class RedisSeriesLock(
    IRedisConnectionProvider connectionProvider,
    IOptions<MarketDataOptions> options,
    ILogger<RedisSeriesLock> logger) : ISeriesLock
{
    /// <summary>The prefix every series lock key carries.</summary>
    public const string KeyPrefix = "pqt:ingest:lock:";

    /// <summary>
    /// Deletes the key only if it still holds this owner's token.
    /// </summary>
    /// <remarks>
    /// A script because the read and the delete must be one step. Done as two
    /// commands, the key can expire and be taken by another pass between them,
    /// and the delete then removes the other pass's lock.
    /// </remarks>
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    /// <summary>
    /// What a run records when Redis cannot answer. Names the store, and never
    /// where it is: the reason is persisted and rendered to operators.
    /// </summary>
    private const string Unreachable = "The ingestion lock store (Redis) could not be reached.";

    /// <summary>
    /// Builds the key that holds one series.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <returns>The key, e.g. <c>pqt:ingest:lock:{id}:1d</c>.</returns>
    public static string KeyFor(InstrumentId instrumentId, BarInterval interval) =>
        $"{KeyPrefix}{instrumentId}:{BarIntervalParser.Describe(interval)}";

    /// <inheritdoc />
    public async Task<ISeriesLease?> TryAcquireAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        CancellationToken cancellationToken = default)
    {
        var ttl = options.Value.BuildIngestionLockTtl();
        var key = KeyFor(instrumentId, interval);
        var token = Guid.NewGuid().ToString("N");

        try
        {
            var connection = await connectionProvider
                .GetConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            // The multiplexer is built with AbortOnConnectFail off, so an
            // unreachable Redis yields a connection that is simply not
            // connected. Asked first, because the command would otherwise wait
            // out its own timeout to say the same thing.
            if (!connection.IsConnected)
            {
                throw new SeriesLockUnavailableException(Unreachable);
            }

            // Checked here and not passed to the command. A SET abandoned
            // after it was sent can still land, and would then hold the series
            // for the whole expiry with no lease anywhere to release it.
            cancellationToken.ThrowIfCancellationRequested();

            var database = connection.GetDatabase();

            var taken = await database
                .StringSetAsync(key, token, ttl, ValueCondition.NotExists)
                .ConfigureAwait(false);

            return taken ? new Lease(database, key, token, ttl, logger) : null;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new SeriesLockUnavailableException(Unreachable, exception);
        }
    }

    /// <summary>
    /// One held series, released by deleting the key if it is still ours.
    /// </summary>
    /// <param name="database">The database the key was set in.</param>
    /// <param name="key">The series key.</param>
    /// <param name="token">This owner's token.</param>
    /// <param name="ttl">The expiry the key was set with.</param>
    /// <param name="logger">Logger for releases that did not go as planned.</param>
    private sealed class Lease(
        IDatabaseAsync database,
        string key,
        string token,
        TimeSpan ttl,
        ILogger logger) : ISeriesLease
    {
        private int _released;

        /// <inheritdoc />
        /// <remarks>
        /// Never throws for Redis. The pass this lease guarded has already
        /// produced its result, and an outage at release must not replace that
        /// result with an exception; the key expires on its own regardless.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return;
            }

            try
            {
                var deleted = (long)await database
                    .ScriptEvaluateAsync(ReleaseScript, [key], [token])
                    .ConfigureAwait(false);

                if (deleted == 0)
                {
                    InfrastructureLog.SeriesLockLost(logger, key, ttl.TotalSeconds);
                }
            }
            catch (Exception exception) when (exception is RedisException or TimeoutException)
            {
                InfrastructureLog.SeriesLockReleaseFailed(logger, exception, key, ttl.TotalSeconds);
            }
        }
    }
}
