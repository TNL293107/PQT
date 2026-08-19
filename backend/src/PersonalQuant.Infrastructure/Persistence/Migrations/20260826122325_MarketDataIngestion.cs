using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketDataIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bars",
                schema: "quant",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    turnover = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ingested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bars", x => new { x.instrument_id, x.interval_minutes, x.opened_at_utc });
                    table.ForeignKey(
                        name: "fk_bars_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_checkpoints",
                schema: "quant",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_bar_opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_succeeded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestion_checkpoints", x => new { x.instrument_id, x.interval_minutes, x.source });
                    table.ForeignKey(
                        name: "fk_ingestion_checkpoints_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_runs",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    requested_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    bars_fetched = table.Column<int>(type: "integer", nullable: false),
                    bars_accepted = table.Column<int>(type: "integer", nullable: false),
                    bars_rejected = table.Column<int>(type: "integer", nullable: false),
                    bars_stored = table.Column<int>(type: "integer", nullable: false),
                    bars_revised = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    raw_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestion_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingestion_runs_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "market_data_raw_batches",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    requested_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    checksum = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_data_raw_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_market_data_raw_batches_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bars_interval_period",
                schema: "quant",
                table: "bars",
                columns: new[] { "interval_minutes", "opened_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_runs_instrument_period",
                schema: "quant",
                table: "ingestion_runs",
                columns: new[] { "instrument_id", "interval_minutes", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_runs_outcome",
                schema: "quant",
                table: "ingestion_runs",
                columns: new[] { "outcome", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_market_data_raw_batches_instrument_period",
                schema: "quant",
                table: "market_data_raw_batches",
                columns: new[] { "instrument_id", "interval_minutes", "fetched_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bars",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "ingestion_checkpoints",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "ingestion_runs",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "market_data_raw_batches",
                schema: "quant");
        }
    }
}
