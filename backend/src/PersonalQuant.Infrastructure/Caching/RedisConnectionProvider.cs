using Microsoft.Extensions.Options;
using PersonalQuant.Infrastructure.Configuration;
using StackExchange.Redis;

namespace PersonalQuant.Infrastructure.Caching;

/// <summary>
/// Creates the Redis multiplexer once, on first use, and shares it for the
/// lifetime of the process.
/// </summary>
/// <remarks>
/// <see cref="ConnectionMultiplexer"/> is expensive to build and designed to
/// be shared, so exactly one instance is created. A <see cref="SemaphoreSlim"/>
/// guards the first connect so that concurrent callers cannot each open their
/// own multiplexer.
/// </remarks>
/// <param name="options">Validated Redis settings.</param>
public sealed class RedisConnectionProvider(IOptions<RedisOptions> options)
    : IRedisConnectionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _connection;

    /// <inheritdoc />
    public async Task<IConnectionMultiplexer> GetConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = _connection;
        if (existing is not null)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _connection ??= await ConnectionMultiplexer
                .ConnectAsync(options.Value.BuildConfiguration())
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
