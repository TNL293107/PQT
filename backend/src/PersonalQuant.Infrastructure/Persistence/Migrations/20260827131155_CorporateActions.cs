using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorporateActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporate_actions",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<int>(type: "integer", nullable: false),
                    ex_date = table.Column<DateOnly>(type: "date", nullable: false),
                    record_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    announced_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_corporate_actions_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_adjustments",
                schema: "quant",
                columns: table => new
                {
                    corporate_action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ex_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference_close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    action_version = table.Column<int>(type: "integer", nullable: false),
                    adjustment_version = table.Column<int>(type: "integer", nullable: false),
                    computed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_factor = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    share_factor = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_adjustments", x => x.corporate_action_id);
                    table.ForeignKey(
                        name: "fk_price_adjustments_action",
                        column: x => x.corporate_action_id,
                        principalSchema: "quant",
                        principalTable: "corporate_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_ex_date",
                schema: "quant",
                table: "corporate_actions",
                column: "ex_date");

            migrationBuilder.CreateIndex(
                name: "ux_corporate_actions_natural_key",
                schema: "quant",
                table: "corporate_actions",
                columns: new[] { "instrument_id", "action_type", "ex_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_adjustments_instrument_ex_date",
                schema: "quant",
                table: "price_adjustments",
                columns: new[] { "instrument_id", "ex_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_adjustments",
                schema: "quant");

            migrationBuilder.DropTable(
                name: "corporate_actions",
                schema: "quant");
        }
    }
}
