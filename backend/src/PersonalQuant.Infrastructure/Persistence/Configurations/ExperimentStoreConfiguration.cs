using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Experiment"/> onto <c>research.experiments</c>.</summary>
/// <remarks>
/// The experiment store of U9. Like <c>research.jobs</c>, its tables are a
/// contract with the Python worker, which writes runs, metrics and artifacts
/// in SQL of its own; <c>quant/tests/fixtures/research_schema.sql</c> mirrors
/// them, and an integration test fails if the two drift.
/// </remarks>
internal sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("experiments", ResearchJobConfiguration.Schema);

        builder.HasKey(experiment => experiment.Id).HasName("pk_experiments");

        builder.Property(experiment => experiment.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ExperimentId(value))
            .ValueGeneratedNever();

        builder.Property(experiment => experiment.Name)
            .HasColumnName("name")
            .HasMaxLength(Experiment.MaxNameLength)
            .IsRequired();

        builder.HasIndex(experiment => experiment.Name)
            .IsUnique()
            .HasDatabaseName("ux_experiments_name");

        builder.Property(experiment => experiment.Hypothesis)
            .HasColumnName("hypothesis")
            .HasMaxLength(Experiment.MaxHypothesisLength);

        builder.Property(experiment => experiment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
    }
}

/// <summary>Maps <see cref="ResearchRun"/> onto <c>research.runs</c>.</summary>
/// <remarks>
/// Every column that makes a run reproducible is required except the ones a
/// run may honestly not have — no model, no seed, no recorded commit. A
/// reproduction has no trial index; a configuration always has both index and
/// count, and the check constraint says so.
/// </remarks>
internal sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("runs", ResearchJobConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_runs_status", "status IN ('succeeded', 'failed')");

            // IS NOT NULL spelled out: a check whose expression is NULL passes,
            // so "trial_index >= 1" alone would admit a configuration with no
            // index at all.
            table.HasCheckConstraint(
                "ck_runs_trial",
                "(reproduces_run_id IS NULL AND trial_index IS NOT NULL AND trial_count IS NOT NULL "
                    + "AND trial_index >= 1 AND trial_count >= trial_index) "
                    + "OR (reproduces_run_id IS NOT NULL AND trial_index IS NULL AND trial_count IS NULL)");

            table.HasCheckConstraint("ck_runs_window", "finished_at_utc >= started_at_utc");
        });

        builder.HasKey(run => run.Id).HasName("pk_runs");

        builder.Property(run => run.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(run => run.ExperimentId)
            .HasColumnName("experiment_id")
            .HasConversion(id => id.Value, value => new ExperimentId(value))
            .IsRequired();

        builder.HasOne<Experiment>()
            .WithMany()
            .HasForeignKey(run => run.ExperimentId)
            .HasConstraintName("fk_runs_experiments")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(run => run.JobId)
            .HasColumnName("job_id")
            .HasConversion(
                new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<ResearchJobId, Guid>(
                    id => id.Value,
                    value => new ResearchJobId(value)));

        // A run outlives its job: jobs are a queue and may be pruned, the
        // experiment log may not.
        builder.HasOne<ResearchJob>()
            .WithMany()
            .HasForeignKey(run => run.JobId)
            .HasConstraintName("fk_runs_jobs")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(run => run.JobId)
            .IsUnique()
            .HasDatabaseName("ux_runs_job_id");

        builder.Property(run => run.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(run => run.Strategy).HasColumnName("strategy").HasMaxLength(64).IsRequired();
        builder.Property(run => run.UniverseCode).HasColumnName("universe_code").HasMaxLength(64).IsRequired();
        builder.Property(run => run.UniverseAsOf).HasColumnName("universe_as_of");
        builder.Property(run => run.Membership).HasColumnName("membership").HasMaxLength(16).IsRequired();
        builder.Property(run => run.DatasetId).HasColumnName("dataset_id").HasMaxLength(64).IsRequired();
        builder.Property(run => run.DatasetVersion).HasColumnName("dataset_version").IsRequired();
        builder.Property(run => run.SchemaVersion).HasColumnName("schema_version").IsRequired();
        builder.Property(run => run.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(run => run.FeatureVersion).HasColumnName("feature_version").HasMaxLength(128);
        builder.Property(run => run.FactorVersion).HasColumnName("factor_version").HasMaxLength(128).IsRequired();
        builder.Property(run => run.ModelVersion).HasColumnName("model_version").HasMaxLength(128);
        builder.Property(run => run.Parameters).HasColumnName("params").HasColumnType("jsonb").IsRequired();
        builder.Property(run => run.ParametersHash).HasColumnName("params_hash").HasMaxLength(64).IsRequired();
        builder.Property(run => run.CodeCommit).HasColumnName("code_commit").HasMaxLength(64);
        builder.Property(run => run.RandomSeed).HasColumnName("random_seed");
        builder.Property(run => run.ResearchEngine).HasColumnName("research_engine").HasMaxLength(64).IsRequired();
        builder.Property(run => run.ResearchEngineVersion)
            .HasColumnName("research_engine_version")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(run => run.TrialIndex).HasColumnName("trial_index");
        builder.Property(run => run.TrialCount).HasColumnName("trial_count");
        builder.Property(run => run.ReproducesRunId).HasColumnName("reproduces_run_id");

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(run => run.ReproducesRunId)
            .HasConstraintName("fk_runs_reproduces")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(run => run.ReproducesRunId).HasDatabaseName("ix_runs_reproduces_run_id");

        builder.Property(run => run.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(run => run.FinishedAtUtc).HasColumnName("finished_at_utc").IsRequired();

        builder.HasIndex(run => new { run.ExperimentId, run.TrialIndex })
            .IsUnique()
            .HasDatabaseName("ux_runs_experiment_trial");

        builder.HasIndex(run => new { run.DatasetId, run.DatasetVersion })
            .HasDatabaseName("ix_runs_dataset");

        builder.HasMany(run => run.Metrics)
            .WithOne()
            .HasForeignKey(metric => metric.RunId)
            .HasConstraintName("fk_run_metrics_runs")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(run => run.Metrics).HasField("_metrics");

        builder.HasMany(run => run.Artifacts)
            .WithOne()
            .HasForeignKey(artifact => artifact.RunId)
            .HasConstraintName("fk_run_artifacts_runs")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(run => run.Artifacts).HasField("_artifacts");
    }
}

/// <summary>Maps <see cref="RunMetric"/> onto <c>research.run_metrics</c>.</summary>
internal sealed class RunMetricConfiguration : IEntityTypeConfiguration<RunMetric>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RunMetric> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("run_metrics", ResearchJobConfiguration.Schema);

        builder.HasKey(metric => new { metric.RunId, metric.Name, metric.Split })
            .HasName("pk_run_metrics");

        builder.Property(metric => metric.RunId).HasColumnName("run_id");
        builder.Property(metric => metric.Name).HasColumnName("name").HasMaxLength(64);
        builder.Property(metric => metric.Split).HasColumnName("split").HasMaxLength(16);
        builder.Property(metric => metric.Value).HasColumnName("value");
    }
}

/// <summary>Maps <see cref="RunArtifact"/> onto <c>research.run_artifacts</c>.</summary>
internal sealed class RunArtifactConfiguration : IEntityTypeConfiguration<RunArtifact>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RunArtifact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("run_artifacts", ResearchJobConfiguration.Schema);

        builder.HasKey(artifact => new { artifact.RunId, artifact.Kind, artifact.Path })
            .HasName("pk_run_artifacts");

        builder.Property(artifact => artifact.RunId).HasColumnName("run_id");
        builder.Property(artifact => artifact.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(artifact => artifact.Path).HasColumnName("path").HasMaxLength(512);
        builder.Property(artifact => artifact.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
    }
}
