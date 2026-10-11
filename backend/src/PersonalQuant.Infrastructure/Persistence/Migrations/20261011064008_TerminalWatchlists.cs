using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalWatchlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "terminal");

            migrationBuilder.CreateTable(
                name: "watchlists",
                schema: "terminal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlists", x => x.id);
                    table.CheckConstraint("ck_watchlists_name", "name ~ '^[a-z0-9][a-z0-9_-]*$'");
                });

            migrationBuilder.CreateTable(
                name: "watchlist_entries",
                schema: "terminal",
                columns: table => new
                {
                    watchlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_entries", x => new { x.watchlist_id, x.instrument_id });
                    table.CheckConstraint("ck_watchlist_entries_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_watchlist_entries_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_watchlist_entries_watchlists",
                        column: x => x.watchlist_id,
                        principalSchema: "terminal",
                        principalTable: "watchlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_entries_instrument_id",
                schema: "terminal",
                table: "watchlist_entries",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_watchlists_name",
                schema: "terminal",
                table: "watchlists",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watchlist_entries",
                schema: "terminal");

            migrationBuilder.DropTable(
                name: "watchlists",
                schema: "terminal");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS terminal;");
        }
    }
}
