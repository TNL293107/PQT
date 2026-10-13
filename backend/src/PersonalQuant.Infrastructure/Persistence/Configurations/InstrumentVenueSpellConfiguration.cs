using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InstrumentVenueSpell"/> onto the <c>instrument_venue_spells</c> table.
/// </summary>
/// <remarks>
/// Append-only, like universe memberships. Overlap between one instrument's
/// spells is refused by an exclusion constraint in the migration, which EF
/// cannot express; a session on two venues at once would have two bands.
/// </remarks>
internal sealed class InstrumentVenueSpellConfiguration : IEntityTypeConfiguration<InstrumentVenueSpell>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InstrumentVenueSpell> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "instrument_venue_spells",
            table => table.HasCheckConstraint(
                "ck_instrument_venue_spells_interval",
                // Half-open: [effective_from, effective_to). A null start is
                // "since before any record", a null end "still there".
                "effective_from IS NULL OR effective_to IS NULL OR effective_to > effective_from"));

        builder.HasKey(spell => spell.Id).HasName("pk_instrument_venue_spells");

        builder.Property(spell => spell.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InstrumentVenueSpellId(value))
            .ValueGeneratedNever();

        builder.Property(spell => spell.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(spell => spell.ExchangeId)
            .HasColumnName("exchange_id")
            .HasConversion(id => id.Value, value => new ExchangeId(value))
            .IsRequired();

        builder.Property(spell => spell.EffectiveFrom).HasColumnName("effective_from");

        builder.Property(spell => spell.EffectiveTo).HasColumnName("effective_to");

        builder.Property(spell => spell.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(spell => spell.InstrumentId)
            .HasConstraintName("fk_instrument_venue_spells_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Exchange>()
            .WithMany()
            .HasForeignKey(spell => spell.ExchangeId)
            .HasConstraintName("fk_instrument_venue_spells_exchange")
            .OnDelete(DeleteBehavior.Restrict);

        // Every read asks for a set of instruments' whole histories.
        builder.HasIndex(spell => spell.InstrumentId)
            .HasDatabaseName("ix_instrument_venue_spells_instrument");
    }
}
