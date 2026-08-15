using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "industry_id",
                schema: "quant",
                table: "instruments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sectors",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sectors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "industries",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_industries", x => x.id);
                    table.ForeignKey(
                        name: "fk_industries_sector",
                        column: x => x.sector_id,
                        principalSchema: "quant",
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_industry",
                schema: "quant",
                table: "instruments",
                column: "industry_id",
                filter: "industry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_industries_sector",
                schema: "quant",
                table: "industries",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "ux_industries_code",
                schema: "quant",
                table: "industries",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sectors_code",
                schema: "quant",
                table: "sectors",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_instruments_industry",
                schema: "quant",
                table: "instruments",
                column: "industry_id",
                principalSchema: "quant",
                principalTable: "industries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_instruments_industry",
                schema: "quant",
                table: "instruments");

            migrationBuilder.DropTable(
                name: "industries",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "sectors",
                schema: "quant");

            migrationBuilder.DropIndex(
                name: "ix_instruments_industry",
                schema: "quant",
                table: "instruments");

            migrationBuilder.DropColumn(
                name: "industry_id",
                schema: "quant",
                table: "instruments");
        }
    }
}
