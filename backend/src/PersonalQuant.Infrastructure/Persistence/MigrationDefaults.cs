namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// Constants shared by the runtime and design-time migration configuration, so
/// the two cannot drift apart and point at different history tables.
/// </summary>
internal static class MigrationDefaults
{
    /// <summary>EF Core's migrations history table name.</summary>
    public const string HistoryTableName = "__EFMigrationsHistory";
}
