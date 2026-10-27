# Phase 7 — definition of done (Sprint 10, item 10.3)

Status: criteria written by Lead on 2026-10-27, before the sprint 10 code, so
the tests target them. Evidence is filled in at the end of sprint 10 (and in
sprint 11 for the stretch items moved there on 2026-10-27). A criterion with no
evidence is not met. The operator declares Phase 7 complete; this document does
not.

## Criteria

| # | Criterion | Evidence required | Evidence |
| --- | --- | --- | --- |
| 1 | The labelled dataset is real (ADR-034) | A VN30 export from a request (`pqt dataset export --request`), verified in .NET and Python; `LabelledDataset.open` prints the window: first and last scored session, rebalances, names per rebalance (min, median, max) | |
| 2 | The catalogue is complete (ADR-033) | All 8 entries in the registry with name, version, inputs, lookback, absence and direction; each with the four §4 tests, gated with every mutant killed | Sprint 9: gates `ba24601` … `5278c5e` (`evidence/sprint-9.md`) |
| 3 | Transforms are versioned and per rebalance (ADR-035) | `raw` and `wz-1`; the hand-computed 5-name case; a change at another session does not change this session's output | |
| 4 | Every factor is evaluated through the job (ADR-036) | One `factor_evaluation` run per feature × transform on the dataset of criterion 1: rank IC and IC series, decay, spread, turnover, coverage, each with its period count; one `factor_correlation` run. The evidence states whether the runs were made before or after the GAS re-import | |
| 5 | The count is honest | Every run in `research.runs` with its `trial_index`; the experiment's latest `trial_count` (its highest `trial_index`) equals the number of configurations run, failed and empty ones included, and re-running a configuration without `reproduces_run_id` counts as a new trial; one evaluation run reproduced with `compare_metrics` identical | |
| 6 | No leak | The look-ahead canary (a feature reading the t + h close) is refused through the job; decay past `to` is dropped; labels are read only outside the harness | |
| 7 | The report says what it can | A first VN30 factor report generated from recorded runs: the window statement, the ADR-033 §5 sentence ("exploratory"), the `trial_count` beside every number, no ranking of factors by significance; the known limits below stated | |
| 8 | Suites green | .NET unit and integration (Docker), Python, frontend: counts, commit and run date; Tester + Audit gates PASS. `Console.test.tsx` is a known flake under load (UI/UX backlog): a failure there is re-run alone before it counts | |
| 9 | Stretch accounted for | `RUNS` view, calibration/EV, Alpha158 and the Qlib run mapper: done in sprint 11, or moved with a reason (decided 2026-10-27, `sprints-8-10-plan.md`) | |

## Known limits that do not block Phase 7

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
