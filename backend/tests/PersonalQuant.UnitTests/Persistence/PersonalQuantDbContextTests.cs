using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public void Model_declares_no_entities_in_phase_0()
    {
        // Guards the Phase 0 boundary: the instrument model is Phase 1 work,
        // and this test is expected to be updated when that phase starts.
        // Arrange
        using var context = CreateContext();

        // Act
        var entityTypes = context.Model.GetEntityTypes();

        // Assert
        Assert.Empty(entityTypes);
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
