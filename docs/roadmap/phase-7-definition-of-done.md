# Phase 7 — definition of done (Sprint 10, item 10.3)

Status: criteria written by Lead on 2026-10-27, before the sprint 10 code, so
the tests target them. Evidence filled by Lead on 2026-10-28 at the end of
sprint 10: criteria 1–7 and 9; criterion 8 waits for the final suite run. Evidence is filled in at the end of sprint 10 (and in
sprint 11 for the stretch items moved there on 2026-10-27). A criterion with no
evidence is not met. The operator declares Phase 7 complete; this document does
not.

## Criteria

| # | Criterion | Evidence required | Evidence |
| --- | --- | --- | --- |
| 1 | The labelled dataset is real (ADR-034) | A VN30 export from a request (`pqt dataset export --request`), verified in .NET and Python; `LabelledDataset.open` prints the window: first and last scored session, rebalances, names per rebalance (min, median, max) || Dataset `0e7d28336af399e1` v1 (schema 5, request present), exported from `tmp/requests/vn30-sprint-9-1.json` at `dc3652e` after the GAS re-import. `pqt dataset verify` (.NET) and the Python reader both pass; content hash `f5c7793a…69ec4`. Window: 2024-08-05 → 2026-07-20, 24 rebalances, 30/30/30 names, re-derived by hand 24/24 (Tester + Audit, `tmp/audit-0e7d28336af399e1/verdict.txt`). `to` is 2026-09-17, the end of sourced VN30 membership, not the last bar (2026-10-12) |
| 2 | The catalogue is complete (ADR-033) | All 8 entries in the registry with name, version, inputs, lookback, absence and direction; each with the four §4 tests, gated with every mutant killed | Sprint 9: gates `ba24601` … `5278c5e` (`evidence/sprint-9.md`) |
| 3 | Transforms are versioned and per rebalance (ADR-035) | `raw` and `wz-1`; the hand-computed 5-name case; a change at another session does not change this session's output || `raw` and `wz-1` in `personal_quant.factors.transforms` (PR #4, `d699d83`). Tests: the hand-computed 5-name case (`test_wz1_on_five_names_matches_the_hand_computation`, and QA + QC's acceptance twin), `test_another_sessions_values_do_not_change_this_ones` and `test_a_cross_section_seen_in_between_does_not_change_the_next_result`. Gate: 10/10 mutants killed, the pooled-panel mutant included (`tmp/audit-d699d83/verdict.txt`) |
| 4 | Every factor is evaluated through the job (ADR-036) | One `factor_evaluation` run per feature × transform on the dataset of criterion 1, for the 7 features with data (`liquidity_20` is out, see Known limits): rank IC and IC series, decay, spread, turnover, coverage, each with its period count; one `factor_correlation` run. The evidence states whether the runs were made before or after the GAS re-import || 14 `factor_evaluation` runs (7 features × `raw`/`wz-1`) and 1 `factor_correlation` run on the dataset of criterion 1, at `2faf336`, recorded 2026-10-28, **after** the GAS re-import (2026-10-27 14:53 UTC). Every metric carries its period count (`evidence/phase-7-vn30-report.md`). Made by a direct handler call, not through the queue (the live worker image predates the handler), so `job_id` is NULL. The Moderator approved the write in Dev's pane |
| 5 | The count is honest | Every run in `research.runs` with its `trial_index`; the experiment's latest `trial_count` (its highest `trial_index`) equals the number of configurations run, failed and empty ones included, and re-running a configuration without `reproduces_run_id` counts as a new trial; one evaluation run reproduced with `compare_metrics` identical || `phase-7-vn30`: 15 runs = 14 trials (`trial_index` 1..14, 14 distinct `params_hash`, 0 failed) + 1 reproduction (`4c7e5080` of trial 1 `ae6813d6`: no index, 29/29 metrics equal). `trial_count` 14. The correlation run is in `phase-7-vn30-correlation`, so the evaluation count is untouched (Tester + Audit, `tmp/audit-phase-7-runs/verdict.txt`). Code: a failed trial counts and a refused payload does not (PR #1 gate, 17/17 mutants, `tmp/audit-abe0be2/verdict.txt`) |
| 6 | No leak | The look-ahead canary (a feature reading the t + h close) is refused through the job; decay past `to` is dropped; labels are read only outside the harness || `test_a_feature_reading_the_label_close_is_refused_through_the_job` (the canary is refused by the harness through the job); `test_decay_drops_a_rebalance_whose_horizon_passes_the_last_session`; labels via `forward_returns` outside the harness (ADR-036 §6). Gate mutants caught: decay on the raw feature, decay at t + h − 1, decay kept past `to` (`tmp/audit-abe0be2/verdict.txt`) |
| 7 | The report says what it can | A first VN30 factor report generated from recorded runs: the window statement, the ADR-033 §5 sentence ("exploratory"), the `trial_count` beside every number, no ranking of factors by significance; the known limits below stated || `evidence/phase-7-vn30-report.md` (PR #8, `5b054de`): every table generated from recorded runs; the window statement and the ADR-033 §5 sentence; `trial_count` 14; no ranking; "no t here is a significance test"; all 5 cells with \|t\| > 2 named; the Known limits. Gate: every cell re-derived from `report-runs.txt` (`tmp/audit-5b054de/verdict.txt`) |
| 8 | Suites green | .NET unit and integration (Docker), Python, frontend: counts, commit and run date; Tester + Audit gates PASS. `Console.test.tsx` is a known flake under load (UI/UX backlog): a failure there is re-run alone before it counts | |
| 9 | Stretch accounted for | `RUNS` view, calibration/EV, Alpha158 and the Qlib run mapper: done in sprint 11, or moved with a reason (decided 2026-10-27, `sprints-8-10-plan.md`) || Moved to sprint 11 on 2026-10-27 by the Moderator (`sprints-8-10-plan.md`): the `RUNS` view, calibration/EV, Alpha158 and the Qlib run mapper. Not done in sprint 10; Phase 7 closes in sprint 11 |

## Known limits that do not block Phase 7

- `liquidity_20` is not evaluated and not in the Phase 7 report (Moderator,
  2026-10-28). CafeF bars carry no turnover (0 of 22,545 daily bars since
  2024), so the feature is absent at every rebalance. It stays in the catalogue
  and comes back when a source reports turnover. If a request still names it,
  its pairs in the `factor_correlation` run are absent (it has no values), and
  the report says so rather than leaving them out silently.
- About 26 monthly cross-sections of at most 30 names; every result is
  exploratory (ADR-033 §5).
- No sector-neutral transform: the dataset carries no point-in-time sector
  (ADR-035 §3).
- No t-statistic for decay horizons longer than the rebalance (ADR-036 §3).
- Treasury-share bias and the 24 hand-transcribed VSDC rows behind eight names'
  share counts (ADR-030, ADR-032).
- GAS value and quality read correctly only after the P1 re-import (sprint 9,
  `evidence/sprint-9.md`); until then GAS `value_ep` and `quality_roe` are
  a stated limit.
