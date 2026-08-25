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
        Message = "Reference data seeding created {ExchangesCreated} exchange(s), {SectorsCreated} sector(s), {IndustriesCreated} industry/industries and {InstrumentsCreated} instrument(s).")]
    public static partial void ReferenceDataSeeded(
        ILogger logger,
        int exchangesCreated,
        int sectorsCreated,
        int industriesCreated,
        int instrumentsCreated);

    [LoggerMessage(
        EventId = 1111,
        Level = LogLevel.Error,
        Message = "Reference data seeding failed. The API will start, but instrument search may return nothing.")]
    public static partial void ReferenceDataSeedFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1120,
        Level = LogLevel.Information,
        Message = "Trading calendar import from {Source} created {Created} closure(s), {AlreadyHeld} already held, {Rejected} rejected.")]
    public static partial void TradingCalendarImportCompleted(
        ILogger logger,
        string source,
        int created,
        int alreadyHeld,
        int rejected);

    [LoggerMessage(
        EventId = 1121,
        Level = LogLevel.Information,
        Message = "Instrument import from {Source} created {Created} instrument(s), matched {Matched}, rejected {Rejected}.")]
    public static partial void InstrumentImportCompleted(
        ILogger logger,
        string source,
        int created,
        int matched,
        int rejected);

    [LoggerMessage(
        EventId = 1122,
        Level = LogLevel.Error,
        Message = "The {Import} import could not be completed. The API will start, but the reference data it depends on is stale.")]
    public static partial void ReferenceDataImportFailed(
        ILogger logger,
        Exception exception,
        string import);

    [LoggerMessage(
        EventId = 1130,
        Level = LogLevel.Information,
        Message = "Ingestion pass covered {Universe} instrument(s): {Ingested} attempted, {Failed} could not be attempted, in {ElapsedMs}ms.")]
    public static partial void IngestionPassCompleted(
        ILogger logger,
        int universe,
        int ingested,
        int failed,
        long elapsedMs);

    [LoggerMessage(
        EventId = 1131,
        Level = LogLevel.Error,
        Message = "An ingestion pass could not be completed. The schedule continues; the next pass will retry.")]
    public static partial void IngestionPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1132,
        Level = LogLevel.Warning,
        Message = "The ingestion schedule covered {Covered} of {Total} listed instruments. The remainder is never ingested until the universe limit is raised.")]
    public static partial void IngestionUniverseTruncated(ILogger logger, int covered, int total);

    [LoggerMessage(
        EventId = 1133,
        Level = LogLevel.Error,
        Message = "The ingestion schedule is misconfigured and will do nothing: {Problem}")]
    public static partial void IngestionScheduleMisconfigured(ILogger logger, string problem);
}
