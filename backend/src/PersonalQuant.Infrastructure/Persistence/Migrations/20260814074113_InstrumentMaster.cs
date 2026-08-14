using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "quant");

            migrationBuilder.CreateTable(
                name: "exchanges",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mic = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchanges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    asset_type = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    listed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    delisted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.ForeignKey(
                        name: "fk_instruments_exchange",
                        column: x => x.exchange_id,
                        principalSchema: "quant",
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exchanges_code",
                schema: "quant",
                table: "exchanges",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_status",
                schema: "quant",
                table: "instruments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_ticker_per_exchange",
                schema: "quant",
                table: "instruments",
                columns: new[] { "exchange_id", "ticker" });

            migrationBuilder.CreateIndex(
                name: "ux_instruments_active_ticker_per_exchange",
                schema: "quant",
                table: "instruments",
                columns: new[] { "exchange_id", "ticker" },
                unique: true,
                filter: "status <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instruments",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "exchanges",
                schema: "quant");
        }
    }
}
