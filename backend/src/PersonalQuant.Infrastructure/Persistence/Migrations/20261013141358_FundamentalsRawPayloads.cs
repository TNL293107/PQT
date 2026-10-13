using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FundamentalsRawPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fundamentals_raw_payloads",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    checksum = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fundamentals_raw_payloads", x => x.id);
                    table.ForeignKey(
                        name: "fk_fundamentals_raw_payloads_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fundamentals_raw_payloads_instrument",
                schema: "quant",
                table: "fundamentals_raw_payloads",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_fundamentals_raw_payloads_filing_checksum",
                schema: "quant",
                table: "fundamentals_raw_payloads",
                columns: new[] { "source", "source_reference", "checksum" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fundamentals_raw_payloads",
                schema: "quant");
        }
    }
}
