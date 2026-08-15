using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Classification;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Industry"/> onto the <c>industries</c> table.
/// </summary>
internal sealed class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Industry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("industries");

        builder.HasKey(industry => industry.Id)
            .HasName("pk_industries");

        builder.Property(industry => industry.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new IndustryId(value))
            .ValueGeneratedNever();

        builder.Property(industry => industry.SectorId)
            .HasColumnName("sector_id")
            .HasConversion(id => id.Value, value => new SectorId(value))
            .IsRequired();

        builder.Property(industry => industry.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => ClassificationCode.Create(value))
            .HasMaxLength(ClassificationCode.MaxLength)
            .IsRequired();

        builder.Property(industry => industry.Name)
            .HasColumnName("name")
            .HasMaxLength(Industry.MaxNameLength)
            .IsRequired();

        builder.Property(industry => industry.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(industry => industry.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasOne<Sector>()
            .WithMany()
            .HasForeignKey(industry => industry.SectorId)
            .HasConstraintName("fk_industries_sector")
            // Taxonomy nodes are reference data and are never removed, so a
            // cascade could only ever be a way to lose them by accident.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(industry => industry.Code)
            .IsUnique()
            .HasDatabaseName("ux_industries_code");

        // Serves "every industry in this sector", which is how a peer group
        // and a sector aggregate are both built.
        builder.HasIndex(industry => industry.SectorId)
            .HasDatabaseName("ix_industries_sector");
    }
}
