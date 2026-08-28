using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="BarRevision"/> onto the <c>bar_revisions</c> table.
/// </summary>
/// <remarks>
/// <para>
/// The observation history that sits beside <c>bars</c>. <c>bars</c> remains the
/// current-best projection and is unchanged by this table's existence: same
/// key, same indexes, same read path.
/// </para>
/// <para>
/// The primary key is the bar's identity plus the revision ordinal, so the same
/// statement cannot be recorded twice and a second writer racing the first
/// fails on the key rather than silently overwriting. That is deliberate — the
/// key is the concurrency guard this pipeline does not otherwise have.
/// </para>
/// </remarks>
internal sealed class BarRevisionConfiguration : IEntityTypeConfiguration<BarRevision>
{
    /// <summary>Total digits stored for a price or a cash amount.</summary>
    private const int MoneyPrecision = 18;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BarRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("bar_revisions");

        builder.HasKey(revision => new
        {
            revision.InstrumentId,
            revision.Interval,
            revision.OpenedAtUtc,
            revision.Revision,
        }).HasName("pk_bar_revisions");

        builder.Property(revision => revision.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(revision => revision.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(revision => revision.OpenedAtUtc)
            .HasColumnName("opened_at_utc")
            .IsRequired();

        builder.Property(revision => revision.Revision)
            .HasColumnName("revision")
            .IsRequired();

        ConfigurePrice(builder, revision => revision.Open, "open");
        ConfigurePrice(builder, revision => revision.High, "high");
        ConfigurePrice(builder, revision => revision.Low, "low");
        ConfigurePrice(builder, revision => revision.Close, "close");

        builder.Property(revision => revision.Volume)
            .HasColumnName("volume")
            .IsRequired();

        builder.Property(revision => revision.Turnover)
            .HasColumnName("turnover")
            .HasPrecision(MoneyPrecision, Price.MaxScale);

        builder.Property(revision => revision.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        // Observation time. Inclusive lower bound, exclusive upper bound, so
        // adjacent revisions share an instant and every instant falls inside
        // exactly one window.
        builder.Property(revision => revision.ObservedFromUtc)
            .HasColumnName("observed_from_utc")
            .IsRequired();

        // Null means currently observed, not unknown.
        builder.Property(revision => revision.ObservedToUtc)
            .HasColumnName("observed_to_utc");

        builder.Property(revision => revision.TransformationVersion)
            .HasColumnName("transformation_version")
            .IsRequired();

        builder.Property(revision => revision.ValidationVersion)
            .HasColumnName("validation_version")
            .IsRequired();

        // Derived from the window, and must not become a column that can
        // disagree with it.
        builder.Ignore(revision => revision.IsCurrent);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(revision => revision.InstrumentId)
            .HasConstraintName("fk_bar_revisions_instrument")
            // Same rule as bars: a delisted security keeps its history.
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the as-of read: locate a period, then walk its statements
        // newest observation first until one covers the requested instant. The
        // primary key orders by revision, which is an ordinal and not a time,
        // so it cannot answer this on its own.
        builder.HasIndex(revision => new
        {
            revision.InstrumentId,
            revision.Interval,
            revision.OpenedAtUtc,
            revision.ObservedFromUtc,
        })
            .IsDescending(false, false, false, true)
            .HasDatabaseName("ix_bar_revisions_observation");
    }

    private static void ConfigurePrice(
        EntityTypeBuilder<BarRevision> builder,
        System.Linq.Expressions.Expression<Func<BarRevision, Price>> property,
        string columnName) =>
        builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(price => price.Value, value => Price.Create(value))
            .HasPrecision(MoneyPrecision, Price.MaxScale)
            .IsRequired();
}
