using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Copies each action's announcement date onto the factor derived from it,
    /// so an as-of read can filter without joining the actions table.
    /// </summary>
    public partial class AdjustmentAnnouncementDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "announced_on",
                schema: "quant",
                table: "price_adjustments",
                type: "date",
                nullable: true);

            // Backfilled rather than left to the next recompute. Until the
            // column is populated a strict as-of read would exclude actions
            // whose announcement date is recorded and simply had not been
            // copied across yet — under-adjusting a series for a reason that
            // is an artefact of this migration rather than a fact about the
            // data.
            migrationBuilder.Sql(
                """
                UPDATE quant.price_adjustments AS a
                   SET announced_on = c.announced_on
                  FROM quant.corporate_actions AS c
                 WHERE c.id = a.corporate_action_id
                   AND c.announced_on IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "announced_on",
                schema: "quant",
                table: "price_adjustments");
        }
    }
}
