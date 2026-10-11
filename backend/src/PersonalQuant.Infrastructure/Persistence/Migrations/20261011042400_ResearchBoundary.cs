using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResearchBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "research");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    claimed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    claimed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_jobs_outcome", "(status = 'succeeded' AND result IS NOT NULL AND finished_at_utc IS NOT NULL) OR (status = 'failed' AND error IS NOT NULL AND finished_at_utc IS NOT NULL) OR (status IN ('queued', 'running') AND finished_at_utc IS NULL)");
                    table.CheckConstraint("ck_jobs_status", "status IN ('queued', 'running', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_created_at_utc",
                schema: "research",
                table: "jobs",
                columns: new[] { "status", "created_at_utc" });

            // The boundary of ADR-016, as grants rather than as a convention.
            // quant_research reads quant and writes research, and holds no
            // write grant on quant at all; default privileges extend the same
            // to every table a later migration creates. The role is created
            // without a login: the backend gives it one only when a password
            // is configured (Research__DatabasePassword), so a deployment that
            // never runs the worker has no extra credential to leak. Roles are
            // cluster-wide, hence the existence check. Needs CREATEROLE, which
            // the compose and test databases' owner has.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'quant_research') THEN
                        CREATE ROLE quant_research NOLOGIN;
                    END IF;
                END
                $$;

                GRANT USAGE ON SCHEMA quant TO quant_research;
                GRANT SELECT ON ALL TABLES IN SCHEMA quant TO quant_research;
                ALTER DEFAULT PRIVILEGES IN SCHEMA quant GRANT SELECT ON TABLES TO quant_research;

                GRANT USAGE ON SCHEMA research TO quant_research;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA research TO quant_research;
                ALTER DEFAULT PRIVILEGES IN SCHEMA research
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO quant_research;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The role is left in place: it is cluster-wide, and another
            // database on the same server may still grant to it.
            migrationBuilder.Sql(
                """
                ALTER DEFAULT PRIVILEGES IN SCHEMA quant REVOKE SELECT ON TABLES FROM quant_research;
                REVOKE SELECT ON ALL TABLES IN SCHEMA quant FROM quant_research;
                REVOKE USAGE ON SCHEMA quant FROM quant_research;
                ALTER DEFAULT PRIVILEGES IN SCHEMA research
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM quant_research;
                """);

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "research");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS research;");
        }
    }
}
