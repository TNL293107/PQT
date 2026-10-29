# Sprint 11 — close Phase 7

Opened by the Moderator on 2026-10-28. Phase 7's evidence is complete for
sprint 10 ([definition of done](phase-7-definition-of-done.md)); the roadmap
still assigns three items to Phase 7, moved here on 2026-10-27. Sprint 11 does
them, re-runs the definition of done, and the operator declares Phase 7.

All four branches start at `b08c375` (`main` = `staging` = `uat` = `develop`).

## Items

| # | Item | Why | Owner |
| --- | --- | --- | --- |
| 11.1 | **Probability calibration and expected-value substrate** (ADR first) | The roadmap assigns it to Phase 7, and Phase 12's prediction-market work depends on it (`advanced-research.md`) | Lead ADR, Dev code |
| 11.2 | **Console `RUNS` view** | Reads `research.runs` through the API, as the CLI does; shows each run's `trial_index`, `trial_count` and metrics. No new store | Dev |
| 11.3 | **Alpha158 behind `FeatureEngine` and the Qlib run mapper** | Roadmap item; optional (it needs the Qlib environment). Native factors already carry the Phase 7 claims | Dev, optional |
| 11.4 | **Read Circular 99 reports** (the fiscal-2026 template) | 99 reports are held with no values, so every name loses fundamentals from fiscal 2026. The largest data gap, and it widens each quarter | Dev, data |
| 11.5 | **Queue `factor_evaluation` from the backend** | Sprint 10's runs were a direct call (`job_id` NULL); `ResearchJobService` has no such job kind | Dev, small |
| 11.6 | **Close**: a fresh labelled export (11.4 changes the fiscal-2026 fundamentals), the evaluations re-run through the queue (11.5) under a **new experiment name** so `trial_count` starts clean instead of adding to `phase-7-vn30`, the Phase 7 report refreshed, and the definition of done re-run with 11.1–11.3 as criterion 9 | Sprint 10's 14-trial evidence goes stale once 11.4 and 11.5 land (QA + QC, 2026-10-28) | Lead, Dev |

Order: 11.4 and 11.1's ADR first, since they gate the rest; then 11.2 and 11.5;
11.3 if time allows. If 11.3 does not fit, criterion 9 records it as moved
with a reason, as it did in sprint 10.

## Rules that stay

- `feature/*` → `develop` → `uat` → `staging` → `main`. Only Lead merges, and
  only after a Tester + Audit PASS. Promoting needs the Moderator.
- QA + QC writes acceptance fixtures before code, as for ADR-034 and ADR-036.
- No Claude attribution in commits or PRs (hooks and CI enforce it).
- Writing to the live database (a purge, a re-import, research runs) needs the
  Moderator's OK first.
