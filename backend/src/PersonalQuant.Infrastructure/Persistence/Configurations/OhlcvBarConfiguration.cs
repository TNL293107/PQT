using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="OhlcvBar"/> onto the <c>bars</c> table.
/// </summary>
/// <remarks>
/// <para>
/// The primary key is the instrument, the interval and the opening instant —
/// no surrogate. Those three are the bar's identity, and a generated key would
/// let the same period be stored twice with nothing at the database level
/// objecting. Deduplication is therefore enforced by the schema rather than by
/// whatever code happens to be writing.
/// </para>
/// <para>
/// Prices are <c>numeric(18,6)</c>. Binary floating point cannot represent a
/// tenth exactly, and a close that comes back a fraction different from the
/// one that went in compounds into returns the market never produced.
/// </para>
/// </remarks>
internal sealed class OhlcvBarConfiguration : IEntityTypeConfiguration<OhlcvBar>
{
    /// <summary>Total digits stored for a price or a cash amount.</summary>
    private const int MoneyPrecision = 18;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OhlcvBar> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("bars");

        builder.HasKey(bar => new { bar.InstrumentId, bar.Interval, bar.OpenedAtUtc })
            .HasName("pk_bars");

        builder.Property(bar => bar.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        // Stored as its length in minutes, which is what the enum's values
        // are. A daily bar is 1440, so a query can order or filter by
        // resolution arithmetically without a lookup table.
        builder.Property(bar => bar.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(bar => bar.OpenedAtUtc)
            .HasColumnName("opened_at_utc")
            .IsRequired();

        ConfigurePrice(builder, bar => bar.Open, "open");
        ConfigurePrice(builder, bar => bar.High, "high");
        ConfigurePrice(builder, bar => bar.Low, "low");
        ConfigurePrice(builder, bar => bar.Close, "close");

        builder.Property(bar => bar.Volume)
            .HasColumnName("volume")
            .IsRequired();

        builder.Property(bar => bar.Turnover)
            .HasColumnName("turnover")
            .HasPrecision(MoneyPrecision, Price.MaxScale);

        builder.Property(bar => bar.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(bar => bar.IngestedAtUtc)
            .HasColumnName("ingested_at_utc")
            .IsRequired();

        builder.Property(bar => bar.RevisedAtUtc)
            .HasColumnName("revised_at_utc");

        builder.Property(bar => bar.Revision)
            .HasColumnName("revision")
            .IsRequired();

        // Derived from the interval and the opening instant, and must not
        // become a column that can disagree with them.
        builder.Ignore(bar => bar.ClosedAtUtc);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(bar => bar.InstrumentId)
            .HasConstraintName("fk_bars_instrument")
            // A delisted security keeps its history: that is the whole reason
            // the instrument master never deletes.
            .OnDelete(DeleteBehavior.Restrict);

        // The primary key already serves "this instrument, this resolution,
        // newest first" because its columns are in that order. This one serves
        // the cross-sectional read — every instrument's bar for a given
        // period — which a screener and a market-breadth panel both do and
        // which the key's column order cannot answer.
        builder.HasIndex(bar => new { bar.Interval, bar.OpenedAtUtc })
            .HasDatabaseName("ix_bars_interval_period");
    }

    private static void ConfigurePrice(
        EntityTypeBuilder<OhlcvBar> builder,
        System.Linq.Expressions.Expression<Func<OhlcvBar, Price>> property,
        string columnName) =>
        builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(price => price.Value, value => Price.Create(value))
            .HasPrecision(MoneyPrecision, Price.MaxScale)
            .IsRequired();
}
