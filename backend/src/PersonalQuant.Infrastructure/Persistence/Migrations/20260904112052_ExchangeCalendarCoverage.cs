using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeCalendarCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "calendar_coverage_from",
                schema: "quant",
                table: "exchanges",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "calendar_coverage_until",
                schema: "quant",
                table: "exchanges",
                type: "date",
                nullable: true);

            // A half-made claim must not be storable. An upper bound without a
            // lower one covers nothing anybody can evaluate, and an end on or
            // before its start claims an empty span — which is a claim to have
            // transcribed nothing, said in a way every reader has to remember
            // to special-case. The aggregate refuses both; so does the table,
            // because the aggregate is not the only thing that can write here.
            migrationBuilder.Sql("""
                ALTER TABLE quant.exchanges
                ADD CONSTRAINT ck_exchanges_calendar_coverage
                CHECK (
                    (calendar_coverage_from IS NULL AND calendar_coverage_until IS NULL)
                    OR (
                        calendar_coverage_from IS NOT NULL
                        AND (
                            calendar_coverage_until IS NULL
                            OR calendar_coverage_until > calendar_coverage_from
                        )
                    )
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE quant.exchanges DROP CONSTRAINT IF EXISTS ck_exchanges_calendar_coverage;");

            migrationBuilder.DropColumn(
                name: "calendar_coverage_from",
                schema: "quant",
                table: "exchanges");

            migrationBuilder.DropColumn(
                name: "calendar_coverage_until",
                schema: "quant",
                table: "exchanges");
        }
    }
}
