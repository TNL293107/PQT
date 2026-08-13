using Microsoft.EntityFrameworkCore;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the terminal's PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// Phase 0 declares no entities. The context exists so that the connection,
/// the schema and the migration pipeline are established and verifiable before
/// any financial model is designed.
/// </para>
/// <para>
/// Entity configuration is discovered from
/// <see cref="IEntityTypeConfiguration{TEntity}"/> implementations in this
/// assembly, so Phase 1 adds a configuration class per entity rather than
/// growing <see cref="OnModelCreating"/>.
/// </para>
/// </remarks>
/// <param name="options">Context options supplied by dependency injection.</param>
public sealed class PersonalQuantDbContext(DbContextOptions<PersonalQuantDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Schema that owns every application table.
    /// </summary>
    /// <remarks>
    /// Application tables are kept out of <c>public</c> so that extensions,
    /// ad-hoc analysis tables and provider tooling cannot collide with the
    /// model that migrations own.
    /// </remarks>
    public const string Schema = "quant";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PersonalQuantDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
