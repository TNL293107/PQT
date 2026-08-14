using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
