# ADR-037: The probability-calibration and expected-value substrate

**Status:** Proposed (Lead, 2026-10-29; for QA + QC review) · **Date:** 2026-10-29 · **Phase:** Sprint 11, item 11.1 (Phase 7)

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

**v1 is a job that takes a model probability and an implied probability per
name per rebalance, scores the model's calibration against realised outcomes,
and prices the disagreement as an expected value net of a declared cost
model. It is recorded as a counted trial like any other.**

### 1. Inputs

- **Event:** binary, `forward return > 0` over the dataset's label horizon.
  Other events are later versions.
- **`p_model`:** the model's probability of the event at rebalance *t*. v1
  has one source: the realised frequency of the event in the name's factor
  quantile over the *previous* rebalances of the same build (a running
  estimate, never using *t*'s own label). It reads the feature through the
  harness and past labels only.
- **`p_implied`:** an implied probability from outside the model. v1 defines
  the interface and ships one source, `p_implied = 0.5` (no view), so the
  substrate runs end to end on data that exists. The covered-warrant and VN30F
  sources named in `advanced-research.md` are the next versions, once an
  instrument model for them exists. Nothing in the job names a venue.
- **Cost model:** `CostModel(fee, spread, slippage)` as fractions of notional,
  a declared parameter of the run, never a constant in code. v1's default is
  the Phase 8 figures where they are known (0.1% sell tax, broker fee) and
  zero spread and slippage, stated as such.

### 2. Calibration

Per build: `p_model` is binned into a fixed small number of bins (v1: 4,
edges 0.25/0.5/0.75), and each bin reports predicted mean, realised frequency
and count. The summary is the **Brier score** over all observations and the
mean absolute reliability gap over bins. Every number carries its observation
count; with at most about 720 observations the curve is exploratory, exactly
as ADR-033 §5 says of the IC tables, and the report says so.

### 3. Expected value

Per name per rebalance:

```
EV = p_model × gain − (1 − p_model) × loss − cost
```

where `gain` and `loss` are the mean realised label of past winners and
losers in the same quantile (running, past rebalances only), and `cost` is the
cost model's round trip. EV is **absent** when any input is absent; it is never
0 by default. The summary is the mean EV and the fraction of positive EV per
rebalance, with counts.

### 4. Point in time

`p_model`, `gain` and `loss` at *t* read features through the harness and
labels of rebalances **before** *t* only. The realised outcome at *t* is read
through `forward_returns`, outside the harness, as ADR-036 §6 requires. The
look-ahead canary applies: a probability built from *t*'s own label must be
refused, and a test plants one.

### 5. Recording

No new table. A run has `strategy = "calibration"`, its own experiment, and
parameters `feature`, `transform_version`, `quantiles`, `calibration_version`
(v1 `"cal-1"`), `cost_model_version` and the cost figures. It takes a
`trial_index` and is reproduced by `reproduce_run`. A changed version or cost
figure is a new trial (ADR-036 §5).

### 6. What Phase 12 gets

The interface only: a `p_implied` input with no contract semantics, the cost
model, and the calibration and EV outputs. Contract correlation, time to
resolution and any live venue are out of scope here and belong to Phase 12.

## Alternatives

- **A fitted calibrator (Platt or isotonic).** Fitting a curve on 720 points
  and reporting it as calibration overstates what the data supports.
  Rejected for v1; the running frequency is the model.
- **Build v1 on covered warrants.** The instrument model does not exist and
  the data is not in the store. Rejected for v1, kept as the next source.
- **A new calibration table.** The run store already holds metrics with counts
  and reproduces them. Rejected.

## Trade-offs

- **`p_implied = 0.5` makes v1's EV a statement about the model alone.** That
  is what the data supports; the value of the substrate is that the second
  source drops in without changing the job.
- **The running estimate is thin early in the window.** The first rebalances
  have few past observations; `p_model` is absent below a minimum count (v1:
  20 observations in the quantile), so early rebalances carry no EV.

## Consequences

- `personal_quant.analytics.calibration`: `calibrate` (bins, Brier, gap) and
  `expected_value`; `CostModel` as a frozen dataclass.
- `worker.handlers`: `calibration_handler`, registered in `default_handlers`;
  `reproduce_run` accepts it.
- Tests (QA + QC fixtures first): a perfectly calibrated synthetic model gives
  Brier equal to the outcome variance and gap 0; a constant 0.5 model gives
  Brier 0.25 on a balanced outcome; the canary (a probability using *t*'s own
  label) is refused; EV is absent when a cost input is absent; a changed cost
  figure is a new trial.
- The Phase 7 report gains one section: calibration and EV for the 7
  features, with the same "exploratory" sentence and `trial_count`.
