using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Universes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "universes",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    coverage_from = table.Column<DateOnly>(type: "date", nullable: true),
                    coverage_until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_universes", x => x.id);
                    table.CheckConstraint("ck_universes_coverage", "(coverage_from IS NULL AND coverage_until IS NULL)\r\nOR (coverage_from IS NOT NULL\r\n    AND (coverage_until IS NULL OR coverage_until > coverage_from))");
                });

            migrationBuilder.CreateTable(
                name: "universe_memberships",
                schema: "quant",
                columns: table => new
                {
                    universe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    announced_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_universe_memberships", x => new { x.universe_id, x.instrument_id, x.effective_from });
                    table.CheckConstraint("ck_universe_memberships_interval", "effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "fk_universe_memberships_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_universe_memberships_universe",
                        column: x => x.universe_id,
                        principalSchema: "quant",
                        principalTable: "universes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_universe_memberships_as_of",
                schema: "quant",
                table: "universe_memberships",
                columns: new[] { "universe_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_universe_memberships_instrument",
                schema: "quant",
                table: "universe_memberships",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_universes_code",
                schema: "quant",
                table: "universes",
                column: "code",
                unique: true);

            // Overlap and re-entry, enforced by the schema rather than by the
            // importer that happens to be running.
            //
            // The primary key already makes a security's spells distinct by
            // start date, which is what allows re-entry: a name demoted in July
            // and restored the following January is two rows, and the gap
            // between them survives. What the key cannot see is that two spells
            // of the same security in the same universe might cover the same
            // dates — a second import run recording a spell nobody closed, or a
            // source that disagrees with an earlier one. Both make the
            // constituent count of an index silently wrong, and neither is
            // visible in any single row.
            //
            // btree_gist supplies the equality operator classes for uuid, which
            // the exclusion constraint needs to combine plain = on the two
            // identifiers with && on the interval. It is a trusted extension on
            // PostgreSQL 13 and later, so a database owner installs it without
            // superuser. It is not dropped on the way down: another object may
            // come to depend on it, and a migration that removes a shared
            // extension takes those with it.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // Half-open, '[)', matching UniverseMembership.WasMemberOn and the
            // check constraint. NULL effective_to makes daterange unbounded
            // above, so two open spells of one security conflict — which is the
            // most common way a membership history goes wrong.
            migrationBuilder.Sql(
                """
                ALTER TABLE quant.universe_memberships
                    ADD CONSTRAINT ex_universe_memberships_no_overlap
                    EXCLUDE USING gist (
                        universe_id WITH =,
                        instrument_id WITH =,
                        daterange(effective_from, effective_to, '[)') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "universe_memberships",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "universes",
                schema: "quant");
        }
    }
}
