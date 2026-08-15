using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Instrument"/> onto the <c>instruments</c> table.
/// </summary>
/// <remarks>
/// Table and column names are snake_case and written out explicitly, for the
/// reasons given on <see cref="ExchangeConfiguration"/>.
/// </remarks>
internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    /// <summary>
    /// Matches <see cref="InstrumentStatus.Delisted"/>. Written as a literal
    /// because an index filter is raw SQL evaluated by PostgreSQL, not by
    /// the CLR.
    /// </summary>
    private const int DelistedStatus = (int)InstrumentStatus.Delisted;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("instruments");

        builder.HasKey(instrument => instrument.Id)
            .HasName("pk_instruments");

        builder.Property(instrument => instrument.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .ValueGeneratedNever();

        builder.Property(instrument => instrument.ExchangeId)
            .HasColumnName("exchange_id")
            .HasConversion(id => id.Value, value => new ExchangeId(value))
            .IsRequired();

        builder.Property(instrument => instrument.Ticker)
            .HasColumnName("ticker")
            .HasConversion(ticker => ticker.Value, value => Ticker.Create(value))
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(instrument => instrument.Name)
            .HasColumnName("name")
            .HasMaxLength(Instrument.MaxNameLength)
            .IsRequired();

        builder.Property(instrument => instrument.Currency)
            .HasColumnName("currency")
            .HasConversion(currency => currency.Value, value => CurrencyCode.Create(value))
            .HasMaxLength(CurrencyCode.Length)
            .IsFixedLength()
            .IsRequired();

        // Stored as integers rather than strings: the values are explicitly
        // numbered in the enum precisely so they are stable on disk.
        builder.Property(instrument => instrument.AssetType)
            .HasColumnName("asset_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(instrument => instrument.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        // --- Search index columns ------------------------------------------
        // Folded copies of the ticker and name, maintained by the aggregate.
        // They exist so that prefix matching runs in the database against a
        // plain column: the canonical Ticker property is value-converted and
        // cannot be pattern-matched, and case- or accent-insensitive matching
        // over Name would depend on the server's collation and could not use
        // an index.
        builder.Property(instrument => instrument.SearchTicker)
            .HasColumnName("search_ticker")
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(instrument => instrument.SearchName)
            .HasColumnName("search_name")
            .HasMaxLength(Instrument.MaxNameLength)
            .IsRequired();

        // Nullable: an index has no industry, and an imported security may
        // not have been mapped yet. A catch-all "unknown" node would make
        // those two indistinguishable and would sum into every sector
        // aggregate as if it were a real classification.
        builder.Property(instrument => instrument.IndustryId)
            .HasColumnName("industry_id")
            // Declared over the non-nullable identifier: EF applies the
            // converter to the value and handles the null case itself, which
            // a lambda pair over IndustryId? cannot express.
            .HasConversion(
                new ValueConverter<IndustryId, Guid>(
                    id => id.Value,
                    value => new IndustryId(value)));

        builder.Property(instrument => instrument.ListedOn)
            .HasColumnName("listed_on");

        builder.Property(instrument => instrument.DelistedOn)
            .HasColumnName("delisted_on");

        builder.Property(instrument => instrument.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(instrument => instrument.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // IsActive is derived from Status and must not become a column that
        // can disagree with it.
        builder.Ignore(instrument => instrument.IsActive);

        builder.HasOne<Exchange>()
            .WithMany()
            .HasForeignKey(instrument => instrument.ExchangeId)
            .HasConstraintName("fk_instruments_exchange")
            // Master data is never removed, so cascading a delete would only
            // ever be a way to lose it by accident.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Industry>()
            .WithMany()
            .HasForeignKey(instrument => instrument.IndustryId)
            .HasConstraintName("fk_instruments_industry")
            .OnDelete(DeleteBehavior.Restrict);

        // Two indexes over the same columns, so each needs a distinct model
        // name: the unnamed overload treats a repeated property set as one
        // index and silently merges the definitions.

        // A ticker identifies at most one *active* instrument per venue.
        // The filter is the point: a delisted ticker is released and can be
        // reassigned to a different issuer, so an unfiltered unique index
        // would reject legitimate history.
        builder.HasIndex(
                instrument => new { instrument.ExchangeId, instrument.Ticker },
                "ActiveTickerPerExchange")
            .IsUnique()
            .HasFilter($"status <> {DelistedStatus}")
            .HasDatabaseName("ux_instruments_active_ticker_per_exchange");

        // Serves ticker-history lookups, which deliberately include delisted
        // rows and so cannot use the partial index above.
        builder.HasIndex(
                instrument => new { instrument.ExchangeId, instrument.Ticker },
                "TickerPerExchange")
            .HasDatabaseName("ix_instruments_ticker_per_exchange");

        builder.HasIndex(instrument => instrument.Status)
            .HasDatabaseName("ix_instruments_status");

        // The pattern operator classes are the point of these two indexes.
        // PostgreSQL only uses a btree index for LIKE 'ABC%' when the index is
        // built with *_pattern_ops, because the default class orders by the
        // database's collation and prefix ranges are not contiguous under it.
        // Without them both indexes would serve equality and nothing else,
        // leaving every prefix search — which is most of them — as a scan.
        builder.HasIndex(instrument => instrument.SearchTicker)
            .HasDatabaseName("ix_instruments_search_ticker")
            .HasOperators("varchar_pattern_ops");

        builder.HasIndex(instrument => instrument.SearchName)
            .HasDatabaseName("ix_instruments_search_name")
            .HasOperators("varchar_pattern_ops");

        // Serves "every instrument in this industry", which is what a peer
        // group and a sector screen are both built from. Partial: most rows
        // are classified, and the ones that are not are never the answer to
        // a classification query.
        builder.HasIndex(instrument => instrument.IndustryId)
            .HasDatabaseName("ix_instruments_industry")
            .HasFilter("industry_id IS NOT NULL");
    }
}
