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
        EventId = 1106,
        Level = LogLevel.Warning,
        Message = "The database is {PendingCount} migration(s) behind this build and automatic "
            + "migration is disabled, so it will stay behind: {Migrations}. Every read and write "
            + "runs against a schema this build was not compiled for. Apply them, or run "
            + "'pqt schema status' to see where the deployment stands.")]
    public static partial void SchemaBehindBuild(ILogger logger, int pendingCount, string migrations);

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
        EventId = 1150,
        Level = LogLevel.Information,
        Message = "Research role {Role} can log in; the quant worker may connect as it.")]
    public static partial void ResearchRoleProvisioned(ILogger logger, string role);

    [LoggerMessage(
        EventId = 1151,
        Level = LogLevel.Information,
        Message = "Research role {Role} has no login: set Research__DatabasePassword to let the quant worker connect.")]
    public static partial void ResearchRoleWithoutLogin(ILogger logger, string role);

    [LoggerMessage(
        EventId = 1152,
        Level = LogLevel.Warning,
        Message = "Research role {Role} does not exist yet; the schema is behind the build.")]
    public static partial void ResearchRoleMissing(ILogger logger, string role);

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
        Message = "Ingestion pass covered {Universe} instrument(s): {Succeeded} succeeded, {Skipped} skipped, {Failed} failed, {Unattempted} could not be attempted, in {ElapsedMs}ms.")]
    public static partial void IngestionPassCompleted(
        ILogger logger,
        int universe,
        int succeeded,
        int skipped,
        int failed,
        int unattempted,
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
        EventId = 1134,
        Level = LogLevel.Information,
        Message = "The ingestion universe is restricted to {Kept} of {Available} listed instrument(s) by configuration: {Tickers}.")]
    public static partial void IngestionUniverseRestricted(
        ILogger logger,
        int kept,
        int available,
        string tickers);

    [LoggerMessage(
        EventId = 1133,
        Level = LogLevel.Error,
        Message = "The ingestion schedule is misconfigured and will do nothing: {Problem}")]
    public static partial void IngestionScheduleMisconfigured(ILogger logger, string problem);

    [LoggerMessage(
        EventId = 1123,
        Level = LogLevel.Information,
        Message = "Corporate action import from {Source} created {Created} action(s), amended {Amended}, rejected {Rejected}.")]
    public static partial void CorporateActionImportCompleted(
        ILogger logger,
        string source,
        int created,
        int amended,
        int rejected);

    [LoggerMessage(
        EventId = 1153,
        Level = LogLevel.Information,
        Message = "Corporate action coverage import from {Source} recorded {Recorded} sweep(s), {AlreadyHeld} already held, rejected {Rejected}.")]
    public static partial void CorporateActionCoverageImportCompleted(
        ILogger logger,
        string source,
        int recorded,
        int alreadyHeld,
        int rejected);

    [LoggerMessage(
        EventId = 1154,
        Level = LogLevel.Warning,
        Message = "Corporate action coverage line {LineNumber} ({Symbol}) was not recorded: {Reason}")]
    public static partial void CorporateActionCoverageRejected(
        ILogger logger,
        int lineNumber,
        string symbol,
        string reason);

    [LoggerMessage(
        EventId = 1125,
        Level = LogLevel.Information,
        Message = "Venue history import from {Source} recorded {Recorded} instrument(s), {Unchanged} unchanged, re-judged {Rejudged} breach(es) on their own venue, rejected {Rejected}.")]
    public static partial void VenueHistoryImportCompleted(
        ILogger logger,
        string source,
        int recorded,
        int unchanged,
        int rejudged,
        int rejected);

    [LoggerMessage(
        EventId = 1126,
        Level = LogLevel.Warning,
        Message = "Venue history for {Symbol} was not recorded ({Reason}): {Detail}")]
    public static partial void VenueHistoryRejected(
        ILogger logger,
        string symbol,
        Application.Instruments.VenueHistoryRejectionReason reason,
        string detail);

    [LoggerMessage(
        EventId = 1124,
        Level = LogLevel.Information,
        Message = "Universe import from {Source} defined {Defined} universe(s), created {Created} spell(s), closed {Closed}, recorded {AnnouncementsRecorded} announcement date(s), rejected {Rejected}; {OpenFindings} coverage finding(s) open.")]
    public static partial void UniverseImportCompleted(
        ILogger logger,
        string source,
        int defined,
        int created,
        int closed,
        int announcementsRecorded,
        int rejected,
        int openFindings);

    [LoggerMessage(
        EventId = 1140,
        Level = LogLevel.Warning,
        Message = "The ingestion lock on {Key} could not be released. It expires on its own within {TtlSeconds}s; until then every pass over that series is skipped as busy.")]
    public static partial void SeriesLockReleaseFailed(
        ILogger logger,
        Exception exception,
        string key,
        double ttlSeconds);

    [LoggerMessage(
        EventId = 1141,
        Level = LogLevel.Warning,
        Message = "The ingestion lock on {Key} had already passed to another owner or expired when this pass released it. The pass outlived the lock's {TtlSeconds}s expiry; raise MarketData:IngestionLockTtlSeconds.")]
    public static partial void SeriesLockLost(ILogger logger, string key, double ttlSeconds);
}
