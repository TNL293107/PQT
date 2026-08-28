using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BarRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bar_revisions",
                schema: "quant",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    turnover = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observed_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transformation_version = table.Column<int>(type: "integer", nullable: false),
                    validation_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bar_revisions", x => new { x.instrument_id, x.interval_minutes, x.opened_at_utc, x.revision });
                    table.ForeignKey(
                        name: "fk_bar_revisions_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bar_revisions_observation",
                schema: "quant",
                table: "bar_revisions",
                columns: new[] { "instrument_id", "interval_minutes", "opened_at_utc", "observed_from_utc" },
                descending: new[] { false, false, false, true });

            // Seeds one open observation window per bar already held.
            //
            // For a bar never restated (revision = 0) this is exact:
            // ingested_at_utc is NOT NULL and is the instant the bar first
            // entered the system, which is precisely when it began to be
            // observed.
            //
            // For a bar with revision >= 1 only the current statement survives
            // in quant.bars, so only the current statement can be seeded, from
            // revised_at_utc. Revisions 0..N-1 are NOT reconstructed and are
            // unavailable from the canonical store: an as-of read for an
            // instant before that bar's last restatement will find nothing for
            // that period, which is the honest answer. Fabricating the missing
            // statements from the current values would produce a history that
            // never happened, and re-normalising them out of
            // quant.market_data_raw_batches would stamp today's rules onto
            // yesterday's payload — a claim about what the current normaliser
            // makes of the old bytes, not about what this system believed. If
            // that reconstruction is ever wanted it belongs in an explicit,
            // auditable backfill tool that records its own provenance, not in
            // a schema migration. See ADR-018.
            migrationBuilder.Sql(
                """
                INSERT INTO quant.bar_revisions (
                    instrument_id, interval_minutes, opened_at_utc, revision,
                    open, high, low, close, volume, turnover, source,
                    observed_from_utc, observed_to_utc,
                    transformation_version, validation_version)
                SELECT
                    instrument_id, interval_minutes, opened_at_utc, revision,
                    open, high, low, close, volume, turnover, source,
                    CASE
                        WHEN revision = 0 THEN ingested_at_utc
                        ELSE revised_at_utc
                    END,
                    NULL,
                    transformation_version, validation_version
                FROM quant.bars;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bar_revisions",
                schema: "quant");
        }
    }
}
