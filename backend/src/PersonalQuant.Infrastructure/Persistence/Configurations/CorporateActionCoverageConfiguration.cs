using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CorporateActionCoverage"/> onto the <c>corporate_action_coverage</c> table (ADR-032).
/// </summary>
/// <remarks>
/// Append-only: one record per issuer and sweep date, and a later sweep is a
/// new row.
/// </remarks>
internal sealed class CorporateActionCoverageConfiguration : IEntityTypeConfiguration<CorporateActionCoverage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CorporateActionCoverage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "corporate_action_coverage",
            table => table.HasCheckConstraint(
                "ck_corporate_action_coverage_interval",
                "swept_on >= complete_from"));

        builder.HasKey(coverage => coverage.Id).HasName("pk_corporate_action_coverage");

        builder.Property(coverage => coverage.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CorporateActionCoverageId(value))
            .ValueGeneratedNever();

        builder.Property(coverage => coverage.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(coverage => coverage.CompleteFrom).HasColumnName("complete_from").IsRequired();

        builder.Property(coverage => coverage.SweptOn).HasColumnName("swept_on").IsRequired();

        builder.Property(coverage => coverage.Source)
            .HasColumnName("source")
            .HasMaxLength(CorporateActionCoverage.MaxSourceLength)
            .IsRequired();

        builder.Property(coverage => coverage.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(coverage => coverage.InstrumentId)
            .HasConstraintName("fk_corporate_action_coverage_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(coverage => new { coverage.InstrumentId, coverage.SweptOn })
            .IsUnique()
            .HasDatabaseName("ux_corporate_action_coverage_instrument_swept_on");
    }
}
