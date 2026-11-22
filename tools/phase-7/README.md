# Phase 7 factor report tooling

Sprint 11.6 rebuilds what sprint 10 kept under the git-ignored `tmp/` and lost
with the D: working copy: the labelled-dataset request, the read-only query of
the run store and the table renderer behind
`docs/roadmap/evidence/phase-7-vn30-report.md`.

The logic lives in `quant/src/personal_quant/research/factor_report.py` so CI
tests it (`quant/tests/test_factor_report.py`, and
`test_factor_report_runs.py` against PostgreSQL). `phase7.py` is a thin entry.
None of these files holds a figure; the outputs do, and they stay in `tmp/`
(ADR-038, ADR-041).

## Steps

Run from the repository root.

1. **Request** (ADR-034 v1), from the catalogue, so every name, version and
   lookback is the code's own. `--to` is the last completed session.

   ```
   uv run --project quant python tools/phase-7/phase7.py request --to 2026-11-13 > tmp/11.6/request.json
   pqt dataset export --request tmp/11.6/request.json
   ```

   Commit the request file beside the evidence once the export is made; it holds
   no figures.

2. **Runs**: one read-only transaction (`BEGIN READ ONLY … ROLLBACK`) over
   `research.runs` and `research.run_metrics`, for the experiment and its
   `-correlation` experiment. The connection string comes from an environment
   variable (`PQT_DATABASE_URL` by default), never from an argument.

   ```
   uv run --project quant python tools/phase-7/phase7.py runs --experiment phase-7-vn30-s11 > tmp/11.6/report-runs.jsonl
   ```

3. **Tables**, rendered from that file only:

   ```
   uv run --project quant python tools/phase-7/phase7.py tables --experiment phase-7-vn30-s11 tmp/11.6/report-runs.jsonl
   ```

   The renderer refuses runs over more than one dataset build. It lists
   factors in catalogue order, never ranked; says `absent` or `failed` rather
   than leaving a cell blank; and its count line states trials, reproductions,
   failed runs and runs recorded without a job.

4. **Vendor share** (ADR-038 amendment), read from the build the runs read,
   with no database:

   ```
   uv run --project quant python tools/phase-7/phase7.py vendor-shares --root <datasets folder> --dataset <id> --version <N>
   ```

   One row per value feature in catalogue order, with its inputs and the
   share of its present rows that rest on a vendor value. A feature whose
   inputs are present on no row reads `—`, never 0. The heading names the
   dataset id, version, schema and content hash. A build before schema 7 is
   refused, since it states no flags.

The prose of the report (window statement, the ADR-033 §5 sentence, the t
values above 2 named, Known limits) is written by QA + QC around these tables.
