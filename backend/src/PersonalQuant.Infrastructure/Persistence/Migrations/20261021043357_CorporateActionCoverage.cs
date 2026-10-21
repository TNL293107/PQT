using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorporateActionCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporate_action_coverage",
                schema: "quant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complete_from = table.Column<DateOnly>(type: "date", nullable: false),
                    swept_on = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_action_coverage", x => x.id);
                    table.CheckConstraint("ck_corporate_action_coverage_interval", "swept_on >= complete_from");
                    table.ForeignKey(
                        name: "fk_corporate_action_coverage_instrument",
                        column: x => x.instrument_id,
                        principalSchema: "quant",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_corporate_action_coverage_instrument_swept_on",
                schema: "quant",
                table: "corporate_action_coverage",
                columns: new[] { "instrument_id", "swept_on" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_action_coverage",
                schema: "quant");
        }
    }
}
