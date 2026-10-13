using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RawFundamentalsPayload"/> onto the <c>fundamentals_raw_payloads</c> table.
/// </summary>
/// <remarks>
/// One row per distinct payload of a filing: the checksum is part of the
/// unique key, so a filing read again unchanged is not stored twice.
/// </remarks>
internal sealed class RawFundamentalsPayloadConfiguration : IEntityTypeConfiguration<RawFundamentalsPayload>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RawFundamentalsPayload> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fundamentals_raw_payloads");

        builder.HasKey(payload => payload.Id).HasName("pk_fundamentals_raw_payloads");

        builder.Property(payload => payload.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(payload => payload.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(payload => payload.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(FinancialReport.MaxSourceReferenceLength)
            .IsRequired();

        builder.Property(payload => payload.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        // Unbounded text, as for market data: the aggregate enforces the size
        // limit.
        builder.Property(payload => payload.Payload).HasColumnName("payload").IsRequired();

        builder.Property(payload => payload.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(RawFundamentalsPayload.MaxContentTypeLength)
            .IsRequired();

        builder.Property(payload => payload.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(payload => payload.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(payload => payload.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(payload => payload.InstrumentId)
            .HasConstraintName("fk_fundamentals_raw_payloads_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payload => new { payload.Source, payload.SourceReference, payload.Checksum })
            .IsUnique()
            .HasDatabaseName("ux_fundamentals_raw_payloads_filing_checksum");

        builder.HasIndex(payload => payload.InstrumentId)
            .HasDatabaseName("ix_fundamentals_raw_payloads_instrument");
    }
}
