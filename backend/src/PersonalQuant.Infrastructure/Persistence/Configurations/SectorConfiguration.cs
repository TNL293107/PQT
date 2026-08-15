using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Classification;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Sector"/> onto the <c>sectors</c> table.
/// </summary>
/// <remarks>
/// Table and column names are snake_case and written out explicitly, for the
/// reasons given on <see cref="ExchangeConfiguration"/>.
/// </remarks>
internal sealed class SectorConfiguration : IEntityTypeConfiguration<Sector>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Sector> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sectors");

        builder.HasKey(sector => sector.Id)
            .HasName("pk_sectors");

        builder.Property(sector => sector.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SectorId(value))
            .ValueGeneratedNever();

        builder.Property(sector => sector.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => ClassificationCode.Create(value))
            .HasMaxLength(ClassificationCode.MaxLength)
            .IsRequired();

        builder.Property(sector => sector.Name)
            .HasColumnName("name")
            .HasMaxLength(Sector.MaxNameLength)
            .IsRequired();

        builder.Property(sector => sector.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(sector => sector.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // The code is how a provider mapping and a seed file refer to a
        // sector, so it must resolve to exactly one row.
        builder.HasIndex(sector => sector.Code)
            .IsUnique()
            .HasDatabaseName("ux_sectors_code");
    }
}
