using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the terminal's PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// Entity configuration is discovered from
/// <see cref="IEntityTypeConfiguration{TEntity}"/> implementations in this
/// assembly, so each entity adds a configuration class rather than growing
/// <see cref="OnModelCreating"/>.
/// </para>
/// <para>
/// The context is also the unit of work. Repositories stage changes; nothing
/// is written until <see cref="SaveChangesAsync(CancellationToken)"/> runs.
/// </para>
/// </remarks>
/// <param name="options">Context options supplied by dependency injection.</param>
public sealed class PersonalQuantDbContext(DbContextOptions<PersonalQuantDbContext> options)
    : DbContext(options), IUnitOfWork
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

    /// <summary>Gets the trading venues.</summary>
    public DbSet<Exchange> Exchanges => Set<Exchange>();

    /// <summary>Gets the instrument master.</summary>
    public DbSet<Instrument> Instruments => Set<Instrument>();

    /// <summary>Gets the upper level of the classification taxonomy.</summary>
    public DbSet<Sector> Sectors => Set<Sector>();

    /// <summary>Gets the lower level of the classification taxonomy.</summary>
    public DbSet<Industry> Industries => Set<Industry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PersonalQuantDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
