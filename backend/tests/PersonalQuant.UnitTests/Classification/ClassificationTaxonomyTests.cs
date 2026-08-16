using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.UnitTests.Classification;

/// <summary>
/// Verifies the two levels of the classification taxonomy.
/// </summary>
public sealed class ClassificationTaxonomyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_registered_sector_carries_its_code_name_and_audit_stamps()
    {
        // Act
        var sector = Sector.Register(ClassificationCode.Create("TECH"), "  Technology  ", Now);

        // Assert
        Assert.False(sector.Id.IsEmpty);
        Assert.Equal("TECH", sector.Code.Value);
        Assert.Equal("Technology", sector.Name);
        Assert.Equal(Now, sector.CreatedAtUtc);
        Assert.Equal(Now, sector.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void A_sector_without_a_name_is_rejected(string? name)
    {
        Assert.Throws<DomainValidationException>(
            () => Sector.Register(ClassificationCode.Create("TECH"), name!, Now));
    }

    [Fact]
    public void A_sector_name_over_the_limit_is_rejected()
    {
        var tooLong = new string('N', Sector.MaxNameLength + 1);

        Assert.Throws<DomainValidationException>(
            () => Sector.Register(ClassificationCode.Create("TECH"), tooLong, Now));
    }

    [Fact]
    public void Renaming_a_sector_advances_only_the_updated_stamp()
    {
        var sector = Sector.Register(ClassificationCode.Create("TECH"), "Tech", Now);
        var later = Now.AddDays(1);

        // Act
        sector.Rename("Technology", later);

        // Assert
        Assert.Equal("Technology", sector.Name);
        Assert.Equal(Now, sector.CreatedAtUtc);
        Assert.Equal(later, sector.UpdatedAtUtc);
    }

    [Fact]
    public void An_industry_belongs_to_the_sector_it_was_registered_under()
    {
        var sectorId = SectorId.New();

        // Act
        var industry = Industry.Register(
            sectorId, ClassificationCode.Create("TECH-SOFT"), "Software", Now);

        // Assert
        Assert.Equal(sectorId, industry.SectorId);
        Assert.Equal("TECH-SOFT", industry.Code.Value);
        Assert.Equal("Software", industry.Name);
    }

    [Fact]
    public void An_industry_without_a_sector_is_rejected()
    {
        // The lower level is only meaningful through its parent: an orphaned
        // industry would classify a security into nothing.
        Assert.Throws<DomainValidationException>(
            () => Industry.Register(
                default, ClassificationCode.Create("TECH-SOFT"), "Software", Now));
    }

    [Fact]
    public void An_industry_without_a_name_is_rejected()
    {
        Assert.Throws<DomainValidationException>(
            () => Industry.Register(
                SectorId.New(), ClassificationCode.Create("TECH-SOFT"), "  ", Now));
    }

    [Fact]
    public void Identifiers_of_the_two_levels_are_issued_unassigned_by_default()
    {
        Assert.True(default(SectorId).IsEmpty);
        Assert.True(default(IndustryId).IsEmpty);
        Assert.False(SectorId.New().IsEmpty);
        Assert.False(IndustryId.New().IsEmpty);
    }
}
