# ADR-037: The probability-calibration and expected-value substrate

**Status:** Proposed (Lead, 2026-10-29; QA + QC review applied: 2 blockers, 5 minor) · **Date:** 2026-10-29 · **Phase:** Sprint 11, item 11.1 (Phase 7)

## Context

The roadmap assigns Phase 7 a "probability calibration and expected-value
substrate", and Phase 12's prediction-market work lists it as a prerequisite
(`advanced-research.md` §1). That document is clear about what is reusable: not
a feed, but a framework that *takes an implied probability from somewhere and
prices the disagreement net of costs*. No legal, liquid prediction market on
Vietnamese equities exists, so v1 cannot be built around one.

What exists: labelled datasets (ADR-034) with a forward-return label per
rebalance; the evaluation job (ADR-036) with its run record, `trial_count` and
reproduction; and the point-in-time split, features through the harness and
outcomes through `forward_returns` outside it.

The sample is the same as every Phase 7 result: about 24 cross-sections of at
most 30 names, so at most about 720 label observations in a build.

## Decision

**v1 is a job that builds a model probability per name per rebalance from
past outcomes only, scores its calibration against realised outcomes, and
prices it as an expected value net of a declared cost model. It is recorded
as a counted trial like any other. The implied-probability input that Phase 12
needs is reserved, not shipped: v1 has no second source, so no number would
depend on it.**

### 1. Inputs

- **Event:** binary, `forward return > 0` over the dataset's label horizon.
  Other events are later versions.
- **`p_model`:** the model's probability of the event at rebalance *t*: the
  realised frequency of the event in the name's factor quantile over the
  rebalances of the same build **whose exit session is at or before *t***.
  The feature is read through the harness; the past labels come from
  `Rebalance.label` of those rebalances only. "Exit at or before *t*" rather
  than "before *t*" matters once `horizon` differs from `rebalance_every`: a
  label that ends after *t* is not known at *t*. Below 20 such observations
  in the quantile, `p_model` is absent.
- **`p_implied`: reserved.** `advanced-research.md` names covered-warrant
  implied moves and the VN30F basis as sources; no instrument model for them
  exists yet, and a placeholder value would be an input nothing depends on.
  When a source exists, a later version adds `mispricing = p_model -
  p_implied` and a contract EV of `p_model - p_implied - cost` (a binary
  contract priced at `p_implied` pays `1 - p_implied` on a win and loses
  `p_implied` otherwise). Nothing in the job names a venue.
- **Cost model:** `CostModel(buy_fee, sell_fee, sell_tax, spread, slippage)`
  as fractions of notional, a declared parameter of the run, never a constant
  in code. The round trip is `buy_fee + sell_fee + sell_tax + spread +
  2 x slippage` (slippage is paid on both legs, the spread once per round
  trip). v1's default, stated in every run: `buy_fee` 0.0015, `sell_fee`
  0.0015, `sell_tax` 0.001 (the Vietnamese sell tax), `spread` 0.001,
  `slippage` 0. These are v1 assumptions, not Phase 8 measurements; Phase 8
  replaces them.

### 2. Calibration

Per build: `p_model` is binned into **3 bins at its own terciles** (a running
frequency of "return > 0" inside a quintile sits near 0.4-0.6, so fixed edges
at 0.25 and 0.75 would leave the outer bins empty). Each bin reports predicted
mean, realised frequency and count; an empty bin is absent with count 0. The
summary is the **Brier score** over all observations and the **count-weighted**
mean absolute reliability gap over bins (the Brier reliability term). Every
number carries its observation count. The observations are not independent
(the same names recur across rebalances, and names are correlated within a
rebalance), so with at most about 720 of them the curve is exploratory,
exactly as ADR-033 §5 says of the IC tables, and the report says so.

### 3. Expected value

Per name per rebalance:

```
EV = p_model × gain − (1 − p_model) × loss − cost
```

where `gain` is the mean realised label of past winners in the same quantile,
`loss` is the **magnitude** of the mean realised label of past losers (the
mean of `-label`, so it is positive and the second term subtracts), both over
the rebalances with exit at or before *t*, and `cost` is the cost model's
round trip. The label is close *t* to close *t + h*, so `gain`, `loss` and
`cost` are all fractions of the entry notional. EV is **absent** when any
input is absent; it is never 0 by default. The summary is the mean EV and the
fraction of positive EV per rebalance, with counts.

### 4. Point in time

Labels never pass through the harness (ADR-036 §6), so the harness cannot
catch a probability that used *t*'s own label. The guard is structural: the
estimator is handed only the rebalances whose **exit is at or before *t***,
and nothing else. The test pins `p_model` at *t* to the value computed by
hand from exactly those labels; a planted estimator that also includes *t*'s
own label gives a different value and fails the test (a mutant to kill),
rather than being "refused". The realised outcome at *t* is read through
`forward_returns`, outside the harness, as today.

### 5. Recording

No new table. A run has `strategy = "calibration"`, under the experiment
`<experiment>-calibration`, and parameters `feature`, `transform_version`,
`quantiles`, `calibration_version` (v1 `"cal-1"`), `cost_model_version` and
the five cost figures. ADR-036 §5 applies verbatim: a trial is one
`parameters_hash` on one dataset version, so every payload field counts; a
failed job counts and a refused payload does not; `reproduce_run` re-executes
it. The report prints the calibration experiment's `trial_count` beside the
features' own (14 in sprint 10), never summed.

### 6. What Phase 12 gets

The cost model, the calibration and EV outputs, and the reserved `p_implied`
slot with the mispricing and contract-EV definitions above. Contract
correlation, time to resolution and any live venue are out of scope here and
belong to Phase 12.

## Alternatives

- **A fitted calibrator (Platt or isotonic).** Fitting a curve on 720 points
  and reporting it as calibration overstates what the data supports.
  Rejected for v1; the running frequency is the model.
- **Build v1 on covered warrants.** The instrument model does not exist and
  the data is not in the store. Rejected for v1, kept as the next source.
- **A new calibration table.** The run store already holds metrics with counts
  and reproduces them. Rejected.

## Trade-offs

- **v1's EV is a statement about the model alone.** That is what the data
  supports; the value of the substrate is that a second source drops in
  without changing the job.
- **The running estimate is thin early in the window.** With 24 rebalances
  and about 6 names per quintile, 20 observations arrive at about the 4th
  rebalance, so roughly 20 of 24 rebalances carry a `p_model` and an EV. The
  report prints that count; it is expected, not a defect.

## Consequences

- `personal_quant.analytics.calibration`: `calibrate` (bins, Brier, gap) and
  `expected_value`; `CostModel` as a frozen dataclass.
- `worker.handlers`: `calibration_handler`, registered in `default_handlers`;
  `reproduce_run` accepts it.
- Tests (QA + QC fixtures first): a constant base-rate model gives Brier equal
  to the outcome variance and gap 0; a model equal to the realised frequency
  per bin gives gap 0 and Brier at most the variance; a constant 0.5 model
  gives Brier 0.25 on a balanced outcome; `p_model` at *t* equals the hand
  value from the rebalances with exit at or before *t*, and an estimator that
  includes *t*'s own label fails it; EV is absent when a cost input is absent;
  a changed cost figure is a new trial.
- The Phase 7 report gains one section: calibration and EV for the 7
  features, with the same "exploratory" sentence and `trial_count`.
