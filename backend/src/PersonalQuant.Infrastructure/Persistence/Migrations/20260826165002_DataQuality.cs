using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "daily_price_limit",
                schema: "quant",
                table: "exchanges",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "transformation_version",
                schema: "quant",
                table: "bars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "validation_version",
                schema: "quant",
                table: "bars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "data_quality_issues",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    session_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    validation_version = table.Column<int>(type: "integer", nullable: false),
                    detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_quality_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_quality_issues_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trading_holidays",
                schema: "quant",
                columns: table => new
                {
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holiday_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_holidays", x => new { x.exchange_id, x.holiday_date });
                    table.ForeignKey(
                        name: "fk_trading_holidays_exchange",
                        column: x => x.exchange_id,
                        principalSchema: "quant",
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bars_unvalidated",
                schema: "quant",
                table: "bars",
                column: "validation_version",
                filter: "validation_version < 1");

            migrationBuilder.CreateIndex(
                name: "ix_data_quality_issues_status",
                schema: "quant",
                table: "data_quality_issues",
                columns: new[] { "status", "session_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_data_quality_issues_session_kind",
                schema: "quant",
                table: "data_quality_issues",
                columns: new[] { "instrument_id", "interval_minutes", "session_at_utc", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trading_holidays_exchange_date",
                schema: "quant",
                table: "trading_holidays",
                columns: new[] { "exchange_id", "holiday_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_quality_issues",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "trading_holidays",
                schema: "quant");

            migrationBuilder.DropIndex(
                name: "ix_bars_unvalidated",
                schema: "quant",
                table: "bars");

            migrationBuilder.DropColumn(
                name: "daily_price_limit",
                schema: "quant",
                table: "exchanges");

            migrationBuilder.DropColumn(
                name: "transformation_version",
                schema: "quant",
                table: "bars");

            migrationBuilder.DropColumn(
                name: "validation_version",
                schema: "quant",
                table: "bars");
        }
    }
}
