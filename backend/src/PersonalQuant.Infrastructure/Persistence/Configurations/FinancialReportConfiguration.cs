using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="FinancialReport"/> onto the <c>financial_reports</c> table.
/// </summary>
/// <remarks>
/// One row per disclosure, append-only. A source's own key identifies a filing
/// within that source, so reading the listing twice records each filing once.
/// </remarks>
internal sealed class FinancialReportConfiguration : IEntityTypeConfiguration<FinancialReport>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FinancialReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "financial_reports",
            table =>
            {
                table.HasCheckConstraint("ck_financial_reports_quarter", "fiscal_quarter BETWEEN 1 AND 4");

                // The kind fixes the closing quarter (ADR-029); the domain checks
                // it too, and this keeps a hand-written row honest.
                table.HasCheckConstraint(
                    "ck_financial_reports_kind_quarter",
                    "(kind <> 2 OR fiscal_quarter = 2) AND (kind <> 3 OR fiscal_quarter = 4)");
            });

        builder.HasKey(report => report.Id).HasName("pk_financial_reports");

        builder.Property(report => report.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new FinancialReportId(value))
            .ValueGeneratedNever();

        builder.Property(report => report.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(report => report.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(report => report.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(FinancialReport.MaxSourceReferenceLength)
            .IsRequired();

        builder.Property(report => report.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(report => report.Scope).HasColumnName("scope").HasConversion<int>().IsRequired();
        builder.Property(report => report.Template).HasColumnName("template").HasConversion<int>().IsRequired();

        builder.ComplexProperty(
            report => report.Period,
            period =>
            {
                period.Property(value => value.Year).HasColumnName("fiscal_year").IsRequired();
                period.Property(value => value.Quarter).HasColumnName("fiscal_quarter").IsRequired();
            });

        builder.Property(report => report.Title)
            .HasColumnName("title")
            .HasMaxLength(FinancialReport.MaxTitleLength)
            .IsRequired();

        builder.Property(report => report.DisclosedOn).HasColumnName("disclosed_on").IsRequired();
        builder.Property(report => report.DisclosedAtUtc).HasColumnName("disclosed_at_utc");

        // Derived once, at recording, from the date, the time and the venue's
        // zone. Stored so an as-of read filters on one column.
        builder.Property(report => report.KnownFromUtc).HasColumnName("known_from_utc").IsRequired();

        builder.Property(report => report.IsCorrection).HasColumnName("is_correction").IsRequired();
        builder.Property(report => report.ValuesFiled).HasColumnName("values_filed").IsRequired();
        builder.Property(report => report.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(report => report.InstrumentId)
            .HasConstraintName("fk_financial_reports_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(report => new { report.Source, report.SourceReference })
            .IsUnique()
            .HasDatabaseName("ux_financial_reports_source_reference");

        // The as-of read: an instrument's reports, in the order they became known.
        builder.HasIndex(report => new { report.InstrumentId, report.KnownFromUtc })
            .HasDatabaseName("ix_financial_reports_instrument_known_from");
    }
}
