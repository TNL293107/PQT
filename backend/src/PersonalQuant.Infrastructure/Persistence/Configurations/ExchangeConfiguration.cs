using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Exchange"/> onto the <c>exchanges</c> table.
/// </summary>
/// <remarks>
/// Table and column names are snake_case and written out explicitly. The
/// Python quant layer queries these tables directly, and PascalCase
/// identifiers in PostgreSQL must be double-quoted at every call site. A
/// global naming convention would achieve the same thing, but it also renames
/// EF's migrations history columns and breaks every database created before it
/// was introduced.
/// </remarks>
internal sealed class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Exchange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("exchanges");

        builder.HasKey(exchange => exchange.Id)
            .HasName("pk_exchanges");

        builder.Property(exchange => exchange.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ExchangeId(value))
            .ValueGeneratedNever();

        builder.Property(exchange => exchange.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => ExchangeCode.Create(value))
            .HasMaxLength(ExchangeCode.MaxLength)
            .IsRequired();

        builder.Property(exchange => exchange.Name)
            .HasColumnName("name")
            .HasMaxLength(Exchange.MaxNameLength)
            .IsRequired();

        builder.Property(exchange => exchange.TimeZoneId)
            .HasColumnName("time_zone_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(exchange => exchange.Mic)
            .HasColumnName("mic")
            .HasMaxLength(Exchange.MicLength);

        // Stored as the fraction the model holds, not as a percentage. One
        // representation, so nothing has to remember which side of the
        // hundred it is on.
        builder.Property(exchange => exchange.DailyPriceLimit)
            .HasColumnName("daily_price_limit")
            .HasConversion(
                new ValueConverter<PriceLimit, decimal>(
                    limit => limit.Fraction,
                    value => PriceLimit.FromPercent(value * 100m)))
            .HasPrecision(6, PriceLimit.MaxScale);

        // Optional by design, and the absence is the meaningful state: a venue
        // that claims nothing has had no calendar transcribed, and every
        // completeness figure over it reports unmeasurable rather than being
        // computed against rows of unknown extent. The lower bound is not
        // nullable, so the two columns cannot express a half-made claim.
        builder.OwnsOne(exchange => exchange.CalendarCoverage, coverage =>
        {
            coverage.Property(span => span.From)
                .HasColumnName("calendar_coverage_from")
                .IsRequired();

            // Null means the claim runs on, not that its end is unknown — which
            // for a Vietnamese venue is a claim nobody can make, since the
            // schedule exists only once an annual notice is published.
            coverage.Property(span => span.Until)
                .HasColumnName("calendar_coverage_until");
        });

        builder.Navigation(exchange => exchange.CalendarCoverage).IsRequired(false);

        builder.Property(exchange => exchange.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(exchange => exchange.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // The operating code is how humans and provider feeds refer to a
        // venue, so it must resolve to exactly one row.
        builder.HasIndex(exchange => exchange.Code)
            .IsUnique()
            .HasDatabaseName("ix_exchanges_code");
    }
}
