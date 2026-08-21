using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InstrumentIdentifier"/> onto the
/// <c>instrument_identifiers</c> table.
/// </summary>
/// <remarks>
/// The two unique indexes at the bottom are the point of this file. They are
/// what make "every provider's spelling of FPT maps to one canonical
/// identifier" a property the database holds, rather than a convention the
/// import pipeline is trusted to follow.
/// </remarks>
internal sealed class InstrumentIdentifierConfiguration
    : IEntityTypeConfiguration<InstrumentIdentifier>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InstrumentIdentifier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("instrument_identifiers");

        builder.HasKey(identifier => identifier.Id)
            .HasName("pk_instrument_identifiers");

        builder.Property(identifier => identifier.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InstrumentIdentifierId(value))
            .ValueGeneratedNever();

        builder.Property(identifier => identifier.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        // Stored as integers rather than strings: the values are explicitly
        // numbered in the enum precisely so they are stable on disk.
        builder.Property(identifier => identifier.Scheme)
            .HasColumnName("scheme")
            .HasConversion<int>()
            .IsRequired();

        // Sized for the longest scheme, which is a provider symbol. An ISIN
        // and a FIGI are twelve characters and their shape is enforced by the
        // value object rather than by the column.
        builder.Property(identifier => identifier.Value)
            .HasColumnName("value")
            .HasMaxLength(IdentifierValue.MaxProviderSymbolLength)
            .IsRequired();

        // Nullable, and the converter is declared over the nullable type so
        // that an absent source stays a NULL rather than becoming an empty
        // string. The two unique indexes below both key off that distinction.
        builder.Property(identifier => identifier.Source)
            .HasColumnName("source")
            .HasConversion(
                new ValueConverter<SourceCode?, string?>(
                    source => source == null ? null : source.Value,
                    value => value == null ? null : SourceCode.Create(value)))
            .HasMaxLength(SourceCode.MaxLength);

        builder.Property(identifier => identifier.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(identifier => identifier.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(identifier => identifier.InstrumentId)
            .HasConstraintName("fk_instrument_identifiers_instrument")
            // An alias outlives a provider dropping it: a price series
            // imported under the symbol stays attached to the instrument, and
            // removing the alias would leave no way to explain how the two
            // were connected.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(identifier => identifier.InstrumentId)
            .HasDatabaseName("ix_instrument_identifiers_instrument");

        // An ISIN or a FIGI names the security everywhere, so it must resolve
        // to one instrument across the whole master.
        //
        // That holds because a Vietnamese security lists on exactly one venue
        // at a time. A cross-listed universe would need this relaxed to
        // include the exchange, and this is the assumption to revisit first
        // when one is added.
        builder.HasIndex(
                identifier => new { identifier.Scheme, identifier.Value },
                "GlobalIdentifier")
            .IsUnique()
            .HasFilter("source IS NULL")
            .HasDatabaseName("ux_instrument_identifiers_global");

        // A provider symbol is unique only within the provider that issued it.
        // Two vendors reuse the same decorated symbol for different
        // securities, so the source is part of the key rather than an
        // attribute beside it.
        builder.HasIndex(
                identifier => new { identifier.Source, identifier.Scheme, identifier.Value },
                "ScopedIdentifier")
            .IsUnique()
            .HasFilter("source IS NOT NULL")
            .HasDatabaseName("ux_instrument_identifiers_scoped");
    }
}
