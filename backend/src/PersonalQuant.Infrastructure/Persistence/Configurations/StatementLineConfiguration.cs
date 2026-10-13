using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="StatementLine"/> onto the <c>statement_lines</c> table.
/// </summary>
/// <remarks>
/// Append-only, like <c>bar_revisions</c>: a line is never updated except to
/// close its observation window. At most one reading of a line of a report is
/// current, which a partial unique index holds.
/// </remarks>
internal sealed class StatementLineConfiguration : IEntityTypeConfiguration<StatementLine>
{
    // Đồng amounts reach 10^16 for the largest banks; EPS keeps four places.
    private const int ValuePrecision = 28;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StatementLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "statement_lines",
            table =>
            {
                table.HasCheckConstraint("ck_statement_lines_quarter", "fiscal_quarter BETWEEN 1 AND 4");

                // A balance is at an instant (span 1), a flow over a span (2, 3).
                table.HasCheckConstraint(
                    "ck_statement_lines_span",
                    "(statement = 1 AND span = 1) OR (statement <> 1 AND span IN (2, 3))");

                table.HasCheckConstraint(
                    "ck_statement_lines_observation",
                    "observed_to_utc IS NULL OR observed_to_utc > observed_from_utc");
            });

        builder.HasKey(line => line.Id).HasName("pk_statement_lines");

        builder.Property(line => line.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new StatementLineId(value))
            .ValueGeneratedNever();

        builder.Property(line => line.ReportId)
            .HasColumnName("report_id")
            .HasConversion(id => id.Value, value => new FinancialReportId(value))
            .IsRequired();

        builder.Property(line => line.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        builder.Property(line => line.Statement).HasColumnName("statement").HasConversion<int>().IsRequired();

        builder.Property(line => line.LineCode)
            .HasColumnName("line_code")
            .HasMaxLength(StatementLine.MaxLineCodeLength)
            .IsRequired();

        builder.ComplexProperty(
            line => line.Period,
            period =>
            {
                period.Property(value => value.Year).HasColumnName("fiscal_year").IsRequired();
                period.Property(value => value.Quarter).HasColumnName("fiscal_quarter").IsRequired();
            });

        builder.Property(line => line.Span).HasColumnName("span").HasConversion<int>().IsRequired();

        builder.Property(line => line.Value)
            .HasColumnName("value")
            .HasPrecision(ValuePrecision, StatementLine.ValueScale)
            .IsRequired();

        builder.Property(line => line.Basis).HasColumnName("basis").HasConversion<int>().IsRequired();

        builder.Property(line => line.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(line => line.Revision).HasColumnName("revision").IsRequired();
        builder.Property(line => line.ObservedFromUtc).HasColumnName("observed_from_utc").IsRequired();

        // Null means currently observed, not unknown.
        builder.Property(line => line.ObservedToUtc).HasColumnName("observed_to_utc");

        // Derived from the window, and must not become a column that can
        // disagree with it.
        builder.Ignore(line => line.IsCurrent);

        builder.HasOne<FinancialReport>()
            .WithMany()
            .HasForeignKey(line => line.ReportId)
            .HasConstraintName("fk_statement_lines_report")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(line => line.InstrumentId)
            .HasConstraintName("fk_statement_lines_instrument")
            .OnDelete(DeleteBehavior.Restrict);

        // A line's identity includes its period, which is a complex property
        // and cannot be indexed here. The migration creates the two unique
        // indexes in SQL: each reading of a line of a report is numbered once
        // (ux_statement_lines_reading), and at most one is current
        // (ux_statement_lines_current, partial on observed_to_utc IS NULL).
        builder.HasIndex(line => line.ReportId)
            .HasDatabaseName("ix_statement_lines_report");

        // The as-of read: an instrument's values across its reports.
        builder.HasIndex(line => line.InstrumentId)
            .HasDatabaseName("ix_statement_lines_instrument");
    }
}
