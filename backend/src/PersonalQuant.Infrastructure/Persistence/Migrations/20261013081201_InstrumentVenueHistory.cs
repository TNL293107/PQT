using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentVenueHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instrument_venue_spells",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_venue_spells", x => x.id);
                    table.CheckConstraint("ck_instrument_venue_spells_interval", "effective_from IS NULL OR effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "fk_instrument_venue_spells_exchange",
                        column: x => x.exchange_id,
                        principalSchema: "quant",
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_instrument_venue_spells_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instrument_venue_spells_exchange_id",
                schema: "quant",
                table: "instrument_venue_spells",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "ix_instrument_venue_spells_instrument",
                schema: "quant",
                table: "instrument_venue_spells",
                column: "instrument_id");

            // One venue per session. btree_gist is installed by the Universes
            // migration; repeated here so this one stands alone. A null start
            // or end makes the range unbounded on that side, so two "since
            // forever" spells, or two open ones, conflict.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.Sql(
                """
                ALTER TABLE quant.instrument_venue_spells
                    ADD CONSTRAINT ex_instrument_venue_spells_no_overlap
                    EXCLUDE USING gist (
                        instrument_id WITH =,
                        daterange(effective_from, effective_to, '[)') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instrument_venue_spells",
                schema: "quant");
        }
    }
}
