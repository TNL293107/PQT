using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CorporateAction"/> onto the <c>corporate_actions</c> table.
/// </summary>
/// <remarks>
/// The unique index is the natural key the import reconciles against: one
/// issuer does not pay two cash dividends going ex on the same day, so a second
/// row for the same instrument, type and ex-date is the same event arriving
/// again rather than a new one.
/// </remarks>
internal sealed class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("corporate_actions");

        builder.HasKey(action => action.Id).HasName("pk_corporate_actions");

        builder.Property(action => action.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CorporateActionId(value))
            .ValueGeneratedNever();

        builder.Property(action => action.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(action => action.Type)
            .HasColumnName("action_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(action => action.ExDate).HasColumnName("ex_date").IsRequired();
        builder.Property(action => action.RecordDate).HasColumnName("record_date");
        builder.Property(action => action.PaymentDate).HasColumnName("payment_date");
        builder.Property(action => action.AnnouncedOn).HasColumnName("announced_on");

        // Nullable and precise. The ratio means a different quantity for each
        // type and the aggregate enforces which types carry one, so a default
        // here would record a number nobody supplied.
        builder.Property(action => action.Ratio)
            .HasColumnName("ratio")
            .HasPrecision(CorporateAction.AmountPrecision, CorporateAction.AmountScale);

        builder.Property(action => action.CashAmount)
            .HasColumnName("cash_amount")
            .HasPrecision(CorporateAction.AmountPrecision, CorporateAction.AmountScale);

        builder.Property(action => action.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(action => action.Version).HasColumnName("version").IsRequired();
        builder.Property(action => action.IsCancelled).HasColumnName("is_cancelled").IsRequired();

        builder.Property(action => action.Note)
            .HasColumnName("note")
            .HasMaxLength(CorporateAction.MaxNoteLength);

        builder.Property(action => action.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(action => action.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        // Derived from the type and the cancellation, and must not become a
        // column that can disagree with either.
        builder.Ignore(action => action.AffectsPrice);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(action => action.InstrumentId)
            .HasConstraintName("fk_corporate_actions_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(action => new { action.InstrumentId, action.Type, action.ExDate })
            .IsUnique()
            .HasDatabaseName("ux_corporate_actions_natural_key");

        // Serves "what goes ex this week", across every instrument.
        builder.HasIndex(action => action.ExDate)
            .HasDatabaseName("ix_corporate_actions_ex_date");
    }
}

/// <summary>
/// Maps <see cref="PriceAdjustment"/> onto the <c>price_adjustments</c> table.
/// </summary>
/// <remarks>
/// Keyed by the action it came from, one to one. Two actions sharing an ex-date
/// keep their own factors and the day's effect is their product, which is what
/// makes it possible to say which half of a paired dividend was wrong.
/// </remarks>
internal sealed class PriceAdjustmentConfiguration : IEntityTypeConfiguration<PriceAdjustment>
{
    /// <summary>Total digits stored for a factor.</summary>
    private const int FactorPrecision = 28;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PriceAdjustment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("price_adjustments");

        builder.HasKey(adjustment => adjustment.CorporateActionId)
            .HasName("pk_price_adjustments");

        builder.Property(adjustment => adjustment.CorporateActionId)
            .HasColumnName("corporate_action_id")
            .HasConversion(id => id.Value, value => new CorporateActionId(value))
            .ValueGeneratedNever();

        builder.Property(adjustment => adjustment.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(adjustment => adjustment.ExDate).HasColumnName("ex_date").IsRequired();

        // The two multipliers are stored as separate columns because they are
        // separate quantities: a cash dividend moves the price and leaves the
        // share count alone.
        builder.ComplexProperty(
            adjustment => adjustment.Factor,
            factor =>
            {
                factor.Property(value => value.Price)
                    .HasColumnName("price_factor")
                    .HasPrecision(FactorPrecision, AdjustmentFactor.Scale)
                    .IsRequired();

                factor.Property(value => value.Shares)
                    .HasColumnName("share_factor")
                    .HasPrecision(FactorPrecision, AdjustmentFactor.Scale)
                    .IsRequired();
            });

        builder.Property(adjustment => adjustment.ReferenceClose)
            .HasColumnName("reference_close")
            .HasConversion(price => price.Value, value => Price.Create(value))
            .HasPrecision(18, Price.MaxScale)
            .IsRequired();

        builder.Property(adjustment => adjustment.ActionVersion)
            .HasColumnName("action_version")
            .IsRequired();

        builder.Property(adjustment => adjustment.AdjustmentVersion)
            .HasColumnName("adjustment_version")
            .IsRequired();

        builder.Property(adjustment => adjustment.ComputedAtUtc)
            .HasColumnName("computed_at_utc")
            .IsRequired();

        builder.HasOne<CorporateAction>()
            .WithOne()
            .HasForeignKey<PriceAdjustment>(adjustment => adjustment.CorporateActionId)
            .HasConstraintName("fk_price_adjustments_action")
            // Actions are never deleted, so this can only ever fire as a guard
            // against a mistake.
            .OnDelete(DeleteBehavior.Restrict);

        // The read: every factor for one instrument, oldest ex-date first.
        builder.HasIndex(adjustment => new { adjustment.InstrumentId, adjustment.ExDate })
            .HasDatabaseName("ix_price_adjustments_instrument_ex_date");
    }
}
