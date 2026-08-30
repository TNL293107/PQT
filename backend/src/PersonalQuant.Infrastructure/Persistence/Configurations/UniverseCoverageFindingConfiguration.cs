using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="UniverseCoverageFinding"/> onto the
/// <c>universe_coverage_findings</c> table.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own rather than a widened <c>data_quality_issues</c>. That
/// one is keyed by instrument, resolution and session, and every one of the
/// three is meaningless here: a coverage gap is a fact about a set, on no
/// particular day, concerning no particular security. Making those columns
/// nullable to fit would weaken the invariants that make a bar finding
/// trustworthy, in order to store something that is not a bar finding.
/// </para>
/// <para>
/// One open finding per universe and kind, enforced by a partial unique index.
/// A review that runs nightly must not raise the same gap again, and the index
/// is what makes that true of the table rather than of the review's own
/// checking.
/// </para>
/// </remarks>
internal sealed class UniverseCoverageFindingConfiguration
    : IEntityTypeConfiguration<UniverseCoverageFinding>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UniverseCoverageFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("universe_coverage_findings");

        builder.HasKey(finding => finding.Id).HasName("pk_universe_coverage_findings");

        builder.Property(finding => finding.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new UniverseCoverageFindingId(value))
            .ValueGeneratedNever();

        builder.Property(finding => finding.UniverseId)
            .HasColumnName("universe_id")
            .HasConversion(id => id.Value, value => new UniverseId(value))
            .IsRequired();

        builder.Property(finding => finding.Kind)
            .HasColumnName("kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(finding => finding.Detail)
            .HasColumnName("detail")
            .HasMaxLength(UniverseCoverageFinding.MaxTextLength)
            .IsRequired();

        builder.Property(finding => finding.DetectedAtUtc)
            .HasColumnName("detected_at_utc")
            .IsRequired();

        builder.Property(finding => finding.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(finding => finding.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(finding => finding.Resolution)
            .HasColumnName("resolution")
            .HasMaxLength(UniverseCoverageFinding.MaxTextLength);

        // Derived from the status, and must not become a column that can
        // disagree with it.
        builder.Ignore(finding => finding.IsOpen);

        builder.HasOne<Universe>()
            .WithMany()
            .HasForeignKey(finding => finding.UniverseId)
            .HasConstraintName("fk_universe_coverage_findings_universe")
            .OnDelete(DeleteBehavior.Restrict);

        // Partial, so a closed finding does not block the same gap being raised
        // again if it comes back — which it can: history sourced, then a claim
        // widened past it.
        builder.HasIndex(finding => new { finding.UniverseId, finding.Kind })
            .IsUnique()
            .HasFilter($"status = {(int)DataQualityIssueStatus.Open}")
            .HasDatabaseName("ux_universe_coverage_findings_open");
    }
}
