using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RawMarketDataBatch"/> onto the
/// <c>market_data_raw_batches</c> table.
/// </summary>
/// <remarks>
/// A separate table from <c>bars</c>, and that separation is the point. The
/// canonical series is read on every chart draw and must stay narrow; the raw
/// payloads are read only when something has to be derived again, and are
/// orders of magnitude larger.
/// </remarks>
internal sealed class RawMarketDataBatchConfiguration
    : IEntityTypeConfiguration<RawMarketDataBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RawMarketDataBatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("market_data_raw_batches");

        builder.HasKey(batch => batch.Id)
            .HasName("pk_market_data_raw_batches");

        builder.Property(batch => batch.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new RawBatchId(value))
            .ValueGeneratedNever();

        builder.Property(batch => batch.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(batch => batch.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(batch => batch.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(batch => batch.RequestedFromUtc)
            .HasColumnName("requested_from_utc")
            .IsRequired();

        builder.Property(batch => batch.RequestedToUtc)
            .HasColumnName("requested_to_utc")
            .IsRequired();

        // Unbounded text rather than a length-capped column: the aggregate
        // enforces the size limit, and a database limit that disagreed with it
        // would reject a payload only after the fetch had already been paid
        // for.
        builder.Property(batch => batch.Payload)
            .HasColumnName("payload")
            .IsRequired();

        builder.Property(batch => batch.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(RawMarketDataBatch.MaxContentTypeLength)
            .IsRequired();

        builder.Property(batch => batch.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(batch => batch.FetchedAtUtc)
            .HasColumnName("fetched_at_utc")
            .IsRequired();

        builder.Property(batch => batch.SizeBytes)
            .HasColumnName("size_bytes")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(batch => batch.InstrumentId)
            .HasConstraintName("fk_market_data_raw_batches_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        // "What did this source send us for this instrument, most recently?"
        // — the query asked when a series looks wrong.
        builder.HasIndex(batch => new { batch.InstrumentId, batch.Interval, batch.FetchedAtUtc })
            .HasDatabaseName("ix_market_data_raw_batches_instrument_period");
    }
}

/// <summary>
/// Maps <see cref="IngestionCheckpoint"/> onto the
/// <c>ingestion_checkpoints</c> table.
/// </summary>
internal sealed class IngestionCheckpointConfiguration
    : IEntityTypeConfiguration<IngestionCheckpoint>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IngestionCheckpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ingestion_checkpoints");

        // The key is what the checkpoint is about. One position per
        // instrument, resolution and source — a surrogate key would permit two
        // rows claiming different positions for the same thing, and nothing
        // could then say which was current.
        builder.HasKey(checkpoint => new
        {
            checkpoint.InstrumentId,
            checkpoint.Interval,
            checkpoint.Source,
        })
            .HasName("pk_ingestion_checkpoints");

        builder.Property(checkpoint => checkpoint.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(checkpoint => checkpoint.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(checkpoint => checkpoint.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(checkpoint => checkpoint.LastBarOpenedAtUtc)
            .HasColumnName("last_bar_opened_at_utc")
            .IsRequired();

        builder.Property(checkpoint => checkpoint.LastSucceededAtUtc)
            .HasColumnName("last_succeeded_at_utc")
            .IsRequired();

        builder.Ignore(checkpoint => checkpoint.ResumeFromUtc);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(checkpoint => checkpoint.InstrumentId)
            .HasConstraintName("fk_ingestion_checkpoints_instrument")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <see cref="IngestionRun"/> onto the <c>ingestion_runs</c> table.
/// </summary>
internal sealed class IngestionRunConfiguration : IEntityTypeConfiguration<IngestionRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IngestionRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ingestion_runs");

        builder.HasKey(run => run.Id)
            .HasName("pk_ingestion_runs");

        builder.Property(run => run.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new IngestionRunId(value))
            .ValueGeneratedNever();

        builder.Property(run => run.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(run => run.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(run => run.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(run => run.RequestedFromUtc)
            .HasColumnName("requested_from_utc")
            .IsRequired();

        builder.Property(run => run.RequestedToUtc)
            .HasColumnName("requested_to_utc")
            .IsRequired();

        builder.Property(run => run.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(run => run.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(run => run.Outcome)
            .HasColumnName("outcome")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(run => run.BarsFetched).HasColumnName("bars_fetched").IsRequired();
        builder.Property(run => run.BarsAccepted).HasColumnName("bars_accepted").IsRequired();
        builder.Property(run => run.BarsRejected).HasColumnName("bars_rejected").IsRequired();
        builder.Property(run => run.BarsStored).HasColumnName("bars_stored").IsRequired();
        builder.Property(run => run.BarsRevised).HasColumnName("bars_revised").IsRequired();
        builder.Property(run => run.Attempts).HasColumnName("attempts").IsRequired();

        builder.Property(run => run.RawBatchId)
            .HasColumnName("raw_batch_id")
            .HasConversion(
                new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<RawBatchId, Guid>(
                    id => id.Value,
                    value => new RawBatchId(value)));

        builder.Property(run => run.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(IngestionRun.MaxFailureReasonLength);

        // Deliberately no foreign key to the raw batch. A skipped or failed
        // run has no payload, and a run whose payload is later pruned must
        // still be readable — the audit trail outliving the bytes it describes
        // is the normal case, not a broken reference.
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(run => run.InstrumentId)
            .HasConstraintName("fk_ingestion_runs_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        // Serves "the recent history for this series", which is how a gap gets
        // explained.
        builder.HasIndex(run => new { run.InstrumentId, run.Interval, run.StartedAtUtc })
            .HasDatabaseName("ix_ingestion_runs_instrument_period");

        // Serves "what is failing right now", across every instrument.
        builder.HasIndex(run => new { run.Outcome, run.StartedAtUtc })
            .HasDatabaseName("ix_ingestion_runs_outcome");
    }
}
