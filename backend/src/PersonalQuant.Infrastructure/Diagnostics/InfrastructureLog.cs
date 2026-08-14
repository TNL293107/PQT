using Microsoft.Extensions.Logging;

namespace PersonalQuant.Infrastructure.Diagnostics;

/// <summary>
/// Source-generated log messages for the infrastructure layer.
/// </summary>
/// <remarks>
/// Compile-time generated delegates avoid the boxing and format-string parsing
/// that the <c>ILogger.Log*</c> extension methods perform on every call. That
/// matters on paths such as health checks and, later, market data ingestion,
/// which run continuously.
/// </remarks>
internal static partial class InfrastructureLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "PostgreSQL health check failed.")]
    public static partial void PostgresHealthCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Redis health check failed.")]
    public static partial void RedisHealthCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Automatic migration is disabled. Run 'dotnet ef database update' to apply pending migrations.")]
    public static partial void AutomaticMigrationDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Information,
        Message = "Database schema is up to date. No migrations pending.")]
    public static partial void SchemaUpToDate(ILogger logger);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Information,
        Message = "Applying {PendingCount} pending migration(s): {Migrations}")]
    public static partial void ApplyingMigrations(ILogger logger, int pendingCount, string migrations);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Information,
        Message = "Database schema migration completed.")]
    public static partial void MigrationCompleted(ILogger logger);

    [LoggerMessage(
        EventId = 1104,
        Level = LogLevel.Warning,
        Message = "Database not ready for migration (attempt {Attempt} of {MaxAttempts}). Retrying in {DelaySeconds}s.")]
    public static partial void MigrationAttemptFailed(
        ILogger logger,
        Exception exception,
        int attempt,
        int maxAttempts,
        double delaySeconds);

    [LoggerMessage(
        EventId = 1105,
        Level = LogLevel.Error,
        Message = "Could not apply migrations after {MaxAttempts} attempts. The API will start, but readiness will report PostgreSQL as unavailable until the schema is up to date.")]
    public static partial void MigrationAbandoned(ILogger logger, Exception exception, int maxAttempts);

    [LoggerMessage(
        EventId = 1110,
        Level = LogLevel.Information,
        Message = "Reference data seeding created {ExchangesCreated} exchange(s) and {InstrumentsCreated} instrument(s).")]
    public static partial void ReferenceDataSeeded(
        ILogger logger,
        int exchangesCreated,
        int instrumentsCreated);

    [LoggerMessage(
        EventId = 1111,
        Level = LogLevel.Error,
        Message = "Reference data seeding failed. The API will start, but instrument search may return nothing.")]
    public static partial void ReferenceDataSeedFailed(ILogger logger, Exception exception);
}
