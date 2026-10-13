using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fundamentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_reports",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    template = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    disclosed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    disclosed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    known_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_correction = table.Column<bool>(type: "boolean", nullable: false),
                    values_filed = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fiscal_quarter = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_reports", x => x.id);
                    table.CheckConstraint("ck_financial_reports_kind_quarter", "(kind <> 2 OR fiscal_quarter = 2) AND (kind <> 3 OR fiscal_quarter = 4)");
                    table.CheckConstraint("ck_financial_reports_quarter", "fiscal_quarter BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_financial_reports_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_lines",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement = table.Column<int>(type: "integer", nullable: false),
                    line_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    span = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<decimal>(type: "numeric(28,4)", precision: 28, scale: 4, nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    observed_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fiscal_quarter = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_lines", x => x.id);
                    table.CheckConstraint("ck_statement_lines_observation", "observed_to_utc IS NULL OR observed_to_utc > observed_from_utc");
                    table.CheckConstraint("ck_statement_lines_quarter", "fiscal_quarter BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_statement_lines_span", "(statement = 1 AND span = 1) OR (statement <> 1 AND span IN (2, 3))");
                    table.ForeignKey(
                        name: "fk_statement_lines_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_lines_report",
                        column: x => x.report_id,
                        principalSchema: "quant",
                        principalTable: "financial_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_reports_instrument_known_from",
                schema: "quant",
                table: "financial_reports",
                columns: new[] { "instrument_id", "known_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_reports_source_reference",
                schema: "quant",
                table: "financial_reports",
                columns: new[] { "source", "source_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_instrument",
                schema: "quant",
                table: "statement_lines",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_report",
                schema: "quant",
                table: "statement_lines",
                column: "report_id");

            // A line's identity includes its period, a complex property EF
            // cannot index. Each reading of a line of a report is numbered
            // once, and at most one reading is current: the same pair of rules
            // bar_revisions keeps for prices.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_statement_lines_reading ON quant.statement_lines
                    (report_id, statement, line_code, fiscal_year, fiscal_quarter, span, revision);
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_statement_lines_current ON quant.statement_lines
                    (report_id, statement, line_code, fiscal_year, fiscal_quarter, span)
                    WHERE observed_to_utc IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statement_lines",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "financial_reports",
                schema: "quant");
        }
    }
}
