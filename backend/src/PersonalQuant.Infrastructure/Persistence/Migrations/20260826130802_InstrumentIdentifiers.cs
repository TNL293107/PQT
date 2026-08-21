using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instrument_identifiers",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheme = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_identifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_instrument_identifiers_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instrument_identifiers_instrument",
                schema: "quant",
                table: "instrument_identifiers",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_instrument_identifiers_global",
                schema: "quant",
                table: "instrument_identifiers",
                columns: new[] { "scheme", "value" },
                unique: true,
                filter: "source IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_instrument_identifiers_scoped",
                schema: "quant",
                table: "instrument_identifiers",
                columns: new[] { "source", "scheme", "value" },
                unique: true,
                filter: "source IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instrument_identifiers",
                schema: "quant");
        }
    }
}
