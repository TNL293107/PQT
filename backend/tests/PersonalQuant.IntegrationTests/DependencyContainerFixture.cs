using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Starts throwaway PostgreSQL and Redis containers once for the whole
/// integration test collection.
/// </summary>
/// <remarks>
/// <para>
/// If no Docker daemon is reachable the fixture records why, and the dependent
/// tests skip with that reason. They are never silently reported as passing.
/// </para>
/// <para>
/// The containers are built inside <see cref="InitializeAsync"/> rather than in
/// field initialisers: Testcontainers probes the Docker endpoint while
/// <c>Build()</c> runs, so constructing them in a field would throw before any
/// handler could turn the failure into a skip.
/// </para>
/// </remarks>
public sealed class DependencyContainerFixture : IAsyncLifetime
{
    private const string DatabaseName = "personal_quant_test";
    private const string DatabaseUser = "quant_test_user";

    // Ephemeral credential for a container that lives for the duration of one
    // test run and is never reachable outside the local Docker network.
    private const string DatabasePassword = "test-only-password";

    private readonly SemaphoreSlim _databaseLock = new(1, 1);
    private readonly HashSet<string> _createdDatabases = new(StringComparer.Ordinal);

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;

    /// <summary>Gets the reason the containers are unavailable, if any.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>Gets the mapped PostgreSQL connection details.</summary>
    public (string Host, int Port, string Database, string Username, string Password) Postgres =>
        _postgres is null
            ? throw new InvalidOperationException("PostgreSQL container is not running.")
            : (_postgres.Hostname, _postgres.GetMappedPublicPort(5432), DatabaseName, DatabaseUser, DatabasePassword);

    /// <summary>Gets the mapped Redis connection details.</summary>
    public (string Host, int Port) Redis =>
        _redis is null
            ? throw new InvalidOperationException("Redis container is not running.")
            : (_redis.Hostname, _redis.GetMappedPublicPort(6379));

    /// <summary>
    /// Creates an additional, empty database on the same server and returns
    /// connection details for it.
    /// </summary>
    /// <remarks>
    /// Every test class in the collection shares one database, which is
    /// normally what you want: it is cheap, and the tests are written to use
    /// distinct tickers and venue codes. A test that seeds the *real*
    /// reference data cannot be written that way — it creates HOSE, HNX and
    /// UPCOM by name, and would collide with any other test that names a
    /// venue. Such a test gets a database to itself instead.
    /// </remarks>
    /// <param name="name">
    /// The database to create. Letters, digits and underscores only; it is
    /// interpolated into a <c>CREATE DATABASE</c> statement, which cannot take
    /// a parameter.
    /// </param>
    /// <returns>Connection details for the new database.</returns>
    public async Task<(string Host, int Port, string Database, string Username, string Password)>
        CreateDatabaseAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_postgres is null)
        {
            throw new InvalidOperationException("PostgreSQL container is not running.");
        }

        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                "A database name may contain letters, digits and underscores only.", nameof(name));
        }

        await _databaseLock.WaitAsync();

        try
        {
            if (_createdDatabases.Add(name))
            {
                var result = await _postgres.ExecScriptAsync($"CREATE DATABASE {name};");

                if (result.ExitCode != 0)
                {
                    _createdDatabases.Remove(name);
                    throw new InvalidOperationException(
                        $"Could not create the '{name}' test database: {result.Stderr}");
                }
            }
        }
        finally
        {
            _databaseLock.Release();
        }

        return (_postgres.Hostname, _postgres.GetMappedPublicPort(5432), name, DatabaseUser, DatabasePassword);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            // Image tags match the ones Docker Compose runs, so a test never
            // passes against a database the development environment does not use.
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase(DatabaseName)
                .WithUsername(DatabaseUser)
                .WithPassword(DatabasePassword)
                .Build();

            _redis = new RedisBuilder("redis:8-alpine").Build();

            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        }
        catch (Exception exception)
        {
            UnavailableReason =
                $"PostgreSQL/Redis test containers could not be started ({exception.GetType().Name}: {exception.Message}). " +
                "Start Docker and re-run to execute the dependency integration tests.";
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        _databaseLock.Dispose();
    }
}

/// <summary>
/// Collection definition that shares one set of containers across every test
/// class that needs real dependencies.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DependencyContainerTests : ICollectionFixture<DependencyContainerFixture>
{
    /// <summary>Collection name.</summary>
    public const string Name = "dependency-containers";
}
