using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ResearchJob"/> onto <c>research.jobs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The one table outside the <c>quant</c> schema, and deliberately so: the
/// <c>quant_research</c> role may write <c>research</c> and only read
/// <c>quant</c> (ADR-016). The quant worker claims, completes and fails rows
/// here in SQL of its own, so the column names, types and constraints are a
/// contract with Python as much as with EF Core — <c>quant/tests/fixtures/research_jobs.sql</c>
/// mirrors them, and an integration test fails if the two drift.
/// </para>
/// <para>
/// Status is text under a check constraint, not an integer: a worker in
/// another language reading <c>running</c> needs no mapping table.
/// </para>
/// </remarks>
internal sealed class ResearchJobConfiguration : IEntityTypeConfiguration<ResearchJob>
{
    /// <summary>The schema the research layer may write.</summary>
    public const string Schema = "research";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResearchJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("jobs", Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_jobs_status",
                "status IN ('queued', 'running', 'succeeded', 'failed')");

            table.HasCheckConstraint("ck_jobs_attempts", "attempts >= 0");

            // A finished job says how it finished, and only that way.
            table.HasCheckConstraint(
                "ck_jobs_outcome",
                "(status = 'succeeded' AND result IS NOT NULL AND finished_at_utc IS NOT NULL) "
                    + "OR (status = 'failed' AND error IS NOT NULL AND finished_at_utc IS NOT NULL) "
                    + "OR (status IN ('queued', 'running') AND finished_at_utc IS NULL)");
        });

        builder.HasKey(job => job.Id).HasName("pk_jobs");

        builder.Property(job => job.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ResearchJobId(value))
            .ValueGeneratedNever();

        builder.Property(job => job.Kind)
            .HasColumnName("kind")
            .HasMaxLength(ResearchJob.MaxKindLength)
            .IsRequired();

        builder.Property(job => job.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(job => job.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<ResearchJobStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(job => job.ClaimedBy)
            .HasColumnName("claimed_by")
            .HasMaxLength(256);

        builder.Property(job => job.ClaimedAtUtc).HasColumnName("claimed_at_utc");

        builder.Property(job => job.LeaseUntilUtc).HasColumnName("lease_until_utc");

        builder.Property(job => job.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(job => job.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(job => job.FinishedAtUtc).HasColumnName("finished_at_utc");

        builder.Property(job => job.Error).HasColumnName("error");

        builder.Property(job => job.Result)
            .HasColumnName("result")
            .HasColumnType("jsonb");

        // The claim query reads the oldest claimable job; this keeps it an
        // index scan however long the finished history grows.
        builder.HasIndex(job => new { job.Status, job.CreatedAtUtc })
            .HasDatabaseName("ix_jobs_status_created_at_utc");
    }
}
