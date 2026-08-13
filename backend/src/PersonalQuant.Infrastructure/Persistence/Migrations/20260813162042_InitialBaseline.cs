using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 0 baseline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Creates no tables. The financial model does not exist yet: instruments
    /// arrive in Phase 1, market data in Phase 2. This migration exists so the
    /// pipeline itself is real and verifiable — applying it creates the
    /// <c>quant</c> schema and the <c>__EFMigrationsHistory</c> table inside
    /// it, which is what every later migration builds on.
    /// </para>
    /// <para>
    /// <c>EnsureSchema</c> is written explicitly rather than left to the
    /// history-table bootstrap, so the schema exists even if the history table
    /// is ever relocated.
    /// </para>
    /// </remarks>
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "quant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSchema(name: "quant");
        }
    }
}
