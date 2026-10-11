using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalQuant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExperimentStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "experiments",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    hypothesis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_experiments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    experiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    universe_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    universe_as_of = table.Column<DateOnly>(type: "date", nullable: true),
                    membership = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dataset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dataset_version = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    feature_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    factor_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    model_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    params_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code_commit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    random_seed = table.Column<long>(type: "bigint", nullable: true),
                    research_engine = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    research_engine_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trial_index = table.Column<int>(type: "integer", nullable: true),
                    trial_count = table.Column<int>(type: "integer", nullable: true),
                    reproduces_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.CheckConstraint("ck_runs_status", "status IN ('succeeded', 'failed')");
                    table.CheckConstraint("ck_runs_trial", "(reproduces_run_id IS NULL AND trial_index IS NOT NULL AND trial_count IS NOT NULL AND trial_index >= 1 AND trial_count >= trial_index) OR (reproduces_run_id IS NOT NULL AND trial_index IS NULL AND trial_count IS NULL)");
                    table.CheckConstraint("ck_runs_window", "finished_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_runs_experiments",
                        column: x => x.experiment_id,
                        principalSchema: "research",
                        principalTable: "experiments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runs_jobs",
                        column: x => x.job_id,
                        principalSchema: "research",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_runs_reproduces",
                        column: x => x.reproduces_run_id,
                        principalSchema: "research",
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "run_artifacts",
                schema: "research",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_artifacts", x => new { x.run_id, x.kind, x.path });
                    table.ForeignKey(
                        name: "fk_run_artifacts_runs",
                        column: x => x.run_id,
                        principalSchema: "research",
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_metrics",
                schema: "research",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    split = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_metrics", x => new { x.run_id, x.name, x.split });
                    table.ForeignKey(
                        name: "fk_run_metrics_runs",
                        column: x => x.run_id,
                        principalSchema: "research",
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_experiments_name",
                schema: "research",
                table: "experiments",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_dataset",
                schema: "research",
                table: "runs",
                columns: new[] { "dataset_id", "dataset_version" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_reproduces_run_id",
                schema: "research",
                table: "runs",
                column: "reproduces_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_runs_experiment_trial",
                schema: "research",
                table: "runs",
                columns: new[] { "experiment_id", "trial_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_runs_job_id",
                schema: "research",
                table: "runs",
                column: "job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_artifacts",
                schema: "research");

            migrationBuilder.DropTable(
                name: "run_metrics",
                schema: "research");

            migrationBuilder.DropTable(
                name: "runs",
                schema: "research");

            migrationBuilder.DropTable(
                name: "experiments",
                schema: "research");
        }
    }
}
