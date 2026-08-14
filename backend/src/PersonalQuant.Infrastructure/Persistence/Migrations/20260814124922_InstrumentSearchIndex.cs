using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_name",
                schema: "quant",
                table: "instruments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "search_ticker",
                schema: "quant",
                table: "instruments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill, then remove the column defaults that AddColumn needed
            // in order to add a NOT NULL column to a populated table. Leaving
            // them would let an INSERT that forgets these columns produce a
            // row that no search can ever return.
            //
            // upper() is a close but not exact reproduction of the folding the
            // application applies: it does not strip Vietnamese diacritics, so
            // an accented name backfilled here is findable by its accented
            // spelling only until the row is next written through the domain.
            // That is acceptable because no code path in the project can have
            // created an instrument row before this migration — there is no
            // import pipeline and no write endpoint — so in every existing
            // deployment the table is empty and the statement is a no-op.
            migrationBuilder.Sql(
                """
                UPDATE quant.instruments
                SET search_ticker = upper(ticker),
                    search_name = upper(name);
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE quant.instruments
                    ALTER COLUMN search_ticker DROP DEFAULT,
                    ALTER COLUMN search_name DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_search_name",
                schema: "quant",
                table: "instruments",
                column: "search_name")
                .Annotation("Npgsql:IndexOperators", new[] { "varchar_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_search_ticker",
                schema: "quant",
                table: "instruments",
                column: "search_ticker")
                .Annotation("Npgsql:IndexOperators", new[] { "varchar_pattern_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_instruments_search_name",
                schema: "quant",
                table: "instruments");

            migrationBuilder.DropIndex(
                name: "ix_instruments_search_ticker",
                schema: "quant",
                table: "instruments");

            migrationBuilder.DropColumn(
                name: "search_name",
                schema: "quant",
                table: "instruments");

            migrationBuilder.DropColumn(
                name: "search_ticker",
                schema: "quant",
                table: "instruments");
        }
    }
}
