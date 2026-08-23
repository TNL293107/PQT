using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TradingHoliday"/> onto the <c>trading_holidays</c> table.
/// </summary>
internal sealed class TradingHolidayConfiguration : IEntityTypeConfiguration<TradingHoliday>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TradingHoliday> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("trading_holidays");

        // The venue and the date are the identity: a venue is either closed on
        // a date or it is not, and a surrogate key would permit two rows
        // disagreeing about it.
        builder.HasKey(holiday => new { holiday.ExchangeId, holiday.Date })
            .HasName("pk_trading_holidays");

        builder.Property(holiday => holiday.ExchangeId)
            .HasColumnName("exchange_id")
            .HasConversion(id => id.Value, value => new ExchangeId(value))
            .IsRequired();

        // A date, not an instant. A closure is a date in the venue's own
        // calendar, and storing it as a timestamp would put it on a different
        // day for anyone reading it from another offset.
        builder.Property(holiday => holiday.Date)
            .HasColumnName("holiday_date")
            .IsRequired();

        builder.Property(holiday => holiday.Name)
            .HasColumnName("name")
            .HasMaxLength(TradingHoliday.MaxNameLength)
            .IsRequired();

        builder.Property(holiday => holiday.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(holiday => holiday.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasOne<Exchange>()
            .WithMany()
            .HasForeignKey(holiday => holiday.ExchangeId)
            .HasConstraintName("fk_trading_holidays_exchange")
            .OnDelete(DeleteBehavior.Restrict);

        // Serves both calendar reads: the window scan, and the horizon lookup
        // that says how far the calendar has been populated.
        builder.HasIndex(holiday => new { holiday.ExchangeId, holiday.Date })
            .HasDatabaseName("ix_trading_holidays_exchange_date");
    }
}

/// <summary>
/// Maps <see cref="DataQualityIssue"/> onto the <c>data_quality_issues</c>
/// table.
/// </summary>
/// <remarks>
/// The unique index is the part that matters. Without it a nightly run
/// re-reading the same range raises the same finding again every night, and a
/// dismissal made on Monday is buried under fresh copies of itself by Friday.
/// </remarks>
internal sealed class DataQualityIssueConfiguration : IEntityTypeConfiguration<DataQualityIssue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DataQualityIssue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("data_quality_issues");

        builder.HasKey(issue => issue.Id)
            .HasName("pk_data_quality_issues");

        builder.Property(issue => issue.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DataQualityIssueId(value))
            .ValueGeneratedNever();

        builder.Property(issue => issue.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(issue => issue.Interval)
            .HasColumnName("interval_minutes")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(issue => issue.SessionAtUtc)
            .HasColumnName("session_at_utc")
            .IsRequired();

        builder.Property(issue => issue.Kind)
            .HasColumnName("kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(issue => issue.Detail)
            .HasColumnName("detail")
            .HasMaxLength(DataQualityIssue.MaxTextLength)
            .IsRequired();

        builder.Property(issue => issue.ValidationVersion)
            .HasColumnName("validation_version")
            .IsRequired();

        builder.Property(issue => issue.DetectedAtUtc)
            .HasColumnName("detected_at_utc")
            .IsRequired();

        builder.Property(issue => issue.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(issue => issue.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(issue => issue.Resolution)
            .HasColumnName("resolution")
            .HasMaxLength(DataQualityIssue.MaxTextLength);

        builder.Ignore(issue => issue.IsOpen);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(issue => issue.InstrumentId)
            .HasConstraintName("fk_data_quality_issues_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        // One finding per series, session and kind — including the resolved
        // ones. A dismissal is a decision about that session, and letting the
        // next run raise a fresh copy would undo it silently.
        builder.HasIndex(issue => new
        {
            issue.InstrumentId,
            issue.Interval,
            issue.SessionAtUtc,
            issue.Kind,
        })
            .IsUnique()
            .HasDatabaseName("ux_data_quality_issues_session_kind");

        // Serves "what is unexplained right now", across every instrument.
        builder.HasIndex(issue => new { issue.Status, issue.SessionAtUtc })
            .HasDatabaseName("ix_data_quality_issues_status");
    }
}
