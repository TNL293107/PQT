# ADR-036: The factor evaluation job — one run per trial, on a labelled dataset

**Status:** Proposed (Lead, 2026-10-27; for QA + QC review) · **Date:** 2026-10-27 · **Phase:** Sprint 10, item 10.2 (Phase 7)

## Context

Sprint 10.2 asks for factor evaluation as worker jobs: IC and rank-IC series,
decay over horizons, quantile spreads, turnover and factor correlation, with
every run in the U9 store counted in `trial_count`. What exists:

- `LabelledDataset` (ADR-034) yields, per rebalance, the feature cross-sections
  through the harness and the label outside it. In v1, `horizon` must equal
  `rebalance_every`.
- `analytics.information.information_coefficient` is Spearman (rank IC) under
  a generic name. There is no Pearson IC.
- The worker has two handlers, `momentum_slice` and `reproduce_run`, and the
  run record is shaped around `SliceResult`.
- Transforms are ADR-035.

## Decision

**A trial is one (feature, transform version, quantiles) on one labelled
dataset. The job evaluates one trial and records one run.**

### 1. The job

Strategy `factor_evaluation`. Payload, all fields required:

```json
{"experiment": "phase-7-vn30", "dataset_id": "…", "dataset_version": 1,
 "feature": "value_ep", "transform_version": "wz-1", "quantiles": 5,
 "decay_horizons": [5, 10, 21, 42, 63]}
```

The feature, its version and lookback come from the dataset's request, so the
payload cannot name a feature the dataset was not built for. Unknown fields are
refused, as the other handlers do.

### 2. Metrics, per rebalance, then summarised

| Metric | Definition |
| --- | --- |
| `rank_ic` | Spearman correlation of the transformed feature with the label; the current function, renamed `rank_information_coefficient` (the old name stays as an alias for the slice) |
| `ic` | Pearson correlation of the same pair; a new function, `pearson_information_coefficient` |
| `spread` | mean label of the top quantile minus the bottom, by `quantile_buckets` |
| `turnover` | share of the top quantile's names not in the previous rebalance's top quantile; absent at the first rebalance |
| `coverage` | names with both a feature value and a label |

Each series is summarised with `summarise` (mean, deviation, IR, t-statistic)
and recorded with its period count. Fewer than 3 names, or no spread, leaves a
period without that metric (as today); the count says so.

### 3. Decay

Decay is the mean rank IC at each horizon in `decay_horizons`, computed with
`forward_returns` on the same build from the same rebalance sessions. It does
not change the dataset's label (ADR-034 keeps one per dataset); the evaluation
varies the horizon, as ADR-034's Trade-offs say.

- A rebalance whose t + h passes the last session is **dropped** for that
  horizon, as ADR-034 §4 does for the label, and each horizon records its own
  period count.
- For h > `rebalance_every` the labels overlap, so their IC are autocorrelated.
  **No t-statistic is reported for them in v1**, only the mean and the count.
- At h = `rebalance_every`, decay must equal the main rank IC exactly. It is a
  test that the two paths agree.

### 4. Factor correlation

Not a per-trial metric: it needs two features. A separate job,
`factor_correlation`, takes a dataset and a transform version. Per rebalance it
computes the Spearman correlation of every pair of features, then averages.
Pooling across sessions is not used, because it mixes the cross-section with
time. It is recorded as one run, which is not a trial: it ranks nothing.

### 5. The run record and `trial_count`

- `strategy = "factor_evaluation"`, `factor_version = "<feature>@<version>"`,
  `parameters` = the payload without `experiment`. `parameters_hash` makes the
  same configuration hash the same.
- Every evaluation run takes a `trial_index`, including one whose metrics are
  all absent: an empty result is still a configuration that was tried.
- A reproduction (same dataset version, parameters, code) is recorded with
  `reproduces_run_id` and no index, as today. A changed transform version,
  quantile count or dataset is a new trial.
- `RunRecord` is built from a plain metrics mapping; the import of
  `SliceResult` moves into the momentum handler, so `experiments.py` no longer
  depends on one strategy.

### 6. The point-in-time split

Features are read only through `LabelledDataset` (the harness); labels and
decay returns only through `forward_returns`, outside it. The transform sees
one cross-section. A test plants a feature that reads the t + h close, and the
job must fail with the harness's error, not score it.

## Alternatives

- **One run for all eight features.** Hides how many hypotheses were tried, and
  `trial_count` is the point of U9. Rejected.
- **Newey–West t-statistics for overlapping horizons.** Correct but a choice of
  lag on about 26 periods; a number that looks precise and is not. Deferred.
- **Decay as several labelled datasets.** Eight builds of the same bars.
  Rejected; ADR-034 already says decay varies the horizon on one build.

## Trade-offs

- **Thin sample.** At most about 26 periods of at most 30 names. Every summary
  carries its period count, and the report calls the results exploratory
  (ADR-033 §5).
- **Turnover of the top quantile only.** It is simple and matches a long-only
  reading. A weight-based turnover comes with the Phase 8 backtester.

## Consequences

- `analytics.information`: `rank_information_coefficient` (renamed, with an
  alias) and `pearson_information_coefficient`.
- `worker.handlers`: `factor_evaluation` and `factor_correlation`, registered in
  `default_handlers`; `reproduce_run` accepts `factor_evaluation` runs.
- `research.experiments`: no import of `momentum_slice`.
- Tests (from QA + QC's plan): a planted factor equal to the label gives IC 1,
  and its negation −1; the look-ahead canary is refused; decay at
  h = rebalance_every equals the main rank IC; the decay past `to` is dropped;
  turnover is 0 for a constant ranking and absent at the first rebalance;
  N configurations give indices 1..N under a concurrent finish, and a
  reproduction leaves the count unchanged.
