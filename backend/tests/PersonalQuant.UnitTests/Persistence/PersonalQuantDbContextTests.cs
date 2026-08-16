using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Infrastructure.Persistence;

namespace PersonalQuant.UnitTests.Persistence;

public sealed class PersonalQuantDbContextTests
{
    [Fact]
    public void Model_uses_the_dedicated_application_schema()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var defaultSchema = context.Model.GetDefaultSchema();

        // Assert
        Assert.Equal(PersonalQuantDbContext.Schema, defaultSchema);
    }

    [Theory]
    [InlineData(typeof(Exchange), "exchanges")]
    [InlineData(typeof(Instrument), "instruments")]
    public void Entities_map_to_snake_case_tables(Type entityType, string expectedTable)
    {
        // The Python quant layer queries these tables directly, and PascalCase
        // identifiers in PostgreSQL must be double-quoted at every call site.
        // Arrange
        using var context = CreateContext();

        // Act
        var table = context.Model.FindEntityType(entityType)?.GetTableName();

        // Assert
        Assert.Equal(expectedTable, table);
    }

    [Theory]
    [InlineData(typeof(Exchange))]
    [InlineData(typeof(Instrument))]
    public void Every_column_is_snake_case(Type entityType)
    {
        // Naming is applied per property rather than by a global convention,
        // which would also rename EF's migrations history columns and break
        // any database created before it was introduced. That makes drift
        // possible, so it is asserted rather than assumed.
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType);

        // Act
        var columns = entity!.GetProperties()
            .Select(property => property.GetColumnName())
            .ToArray();

        // Assert
        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, column => column.Any(char.IsAsciiLetterUpper));
    }

    [Fact]
    public void The_migrations_history_table_keeps_its_default_column_names()
    {
        // Guards the upgrade path. EF reads this table before it can apply a
        // migration, so renaming its columns strands every database created
        // earlier with no way to migrate forward. A global snake_case
        // convention does exactly that, which is why naming is configured per
        // property instead.
        // Arrange
        using var context = CreateContext();

        // Act
        var createScript = context.GetService<IHistoryRepository>().GetCreateScript();

        // Assert
        Assert.Contains("MigrationId", createScript, StringComparison.Ordinal);
        Assert.Contains("ProductVersion", createScript, StringComparison.Ordinal);
        Assert.DoesNotContain("migration_id", createScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Derived_activity_flag_is_not_persisted()
    {
        // IsActive is computed from Status. A column would be able to disagree
        // with it.
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Instrument));

        // Act
        var isActive = entity!.FindProperty(nameof(Instrument.IsActive));

        // Assert
        Assert.Null(isActive);
    }

    [Fact]
    public void Active_ticker_uniqueness_is_scoped_to_non_delisted_rows()
    {
        // An unfiltered unique index would reject a ticker legitimately
        // reassigned to a new issuer after the previous one delisted.
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Instrument));

        // Act
        var index = entity!.GetIndexes()
            .Single(candidate => candidate.IsUnique);

        // Assert
        Assert.Equal("ux_instruments_active_ticker_per_exchange", index.GetDatabaseName());
        Assert.Equal($"status <> {(int)InstrumentStatus.Delisted}", index.GetFilter());
    }

    [Fact]
    public void Instruments_cannot_be_removed_by_cascading_from_an_exchange()
    {
        // Master data is never deleted; a cascade would only ever be a way to
        // lose it by accident. Asserted over every relationship the instrument
        // has rather than a named one, so a foreign key added later cannot
        // introduce a cascade without this failing.
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Instrument));

        // Act
        var foreignKeys = entity!.GetForeignKeys().ToList();

        // Assert
        Assert.NotEmpty(foreignKeys);
        Assert.All(
            foreignKeys,
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    private static PersonalQuantDbContext CreateContext()
    {
        // The model is built without connecting; no server is involved.
        var options = new DbContextOptionsBuilder<PersonalQuantDbContext>()
            .UseNpgsql("Host=localhost;Database=personal_quant;Username=quant_user")
            .Options;

        return new PersonalQuantDbContext(options);
    }
}
