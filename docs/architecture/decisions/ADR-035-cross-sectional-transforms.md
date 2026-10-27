# ADR-035: Cross-sectional transforms — winsorise and z-score, per rebalance, versioned

**Status:** Proposed (Lead, 2026-10-27; QA + QC review applied) · **Date:** 2026-10-27 · **Phase:** Sprint 10, item 10.1 (Phase 7)

## Context

ADR-033 keeps a feature a raw, reproducible quantity and leaves cross-sectional
transforms to the evaluation, "versioned there". Sprint 10.1 asks for winsorise,
z-score and an optional sector-neutral step. Three facts shape the decision:

- A cross-section is at most 30 names (VN30), and about 26 of them over the
  window (ADR-034 §6). Percentile limits on 30 names move one or two names.
- The canonical dataset carries no sector. The two-level taxonomy lives in the
  instrument master, read as of today, not per session.
- The QA + QC sprint 10 plan names the main risk: a transform fitted over the
  pooled panel leaks later dispersion into earlier sessions.

## Decision

**A transform is a pure function of one rebalance's cross-section. v1 is
winsorise, then z-score. Sector-neutral is not in v1.**

### 1. The function

`transform(cross_section: Mapping[str, float]) -> Mapping[str, float]`, applied
to one feature at one rebalance, after the harness has produced it. It reads
nothing else: not other sessions, not other features, not the label.

1. **Minimum names.** With fewer than 5 names present the result is empty
   (absent), not transformed. Five is the smallest set on which a median and a
   deviation carry anything.
2. **Winsorise** at median ± 3 × MAD × 1.4826, the scaled median absolute
   deviation. Values beyond are set to the limit. Robust on 30 names, where a
   percentile clip would just remove the top and bottom name. If MAD is 0, the
   values are left as they are: four equal values and one outlier are then
   z-scored unclipped. Accepted; the outlier still ranks last or first, and a
   cross-section that degenerate is rare among 30 names.
3. **Z-score** with the sample mean and the sample standard deviation (n − 1)
   of the winsorised values. If the deviation is 0, the result is empty.
4. **Absent stays absent.** A name not in the input is not in the output; no
   name is given 0 or the mean.

The output keys equal the input keys, or none.

### 2. The version

`transform_version = "wz-1"` (winsorise 3 MAD, z-score, min 5). Any change to a
rule, a constant or the order is a new version. `"raw"` names the identity
transform, so a run states which one it used. Both are recorded in every run's
parameters (ADR-036) and so are part of what a trial is.

### 3. Sector-neutral: deferred

Sector-neutral needs the sector **at the rebalance**. The dataset does not carry
it, and reading today's classification from the instrument master would put
present knowledge into the past. It is added when the dataset exports a
point-in-time sector (a manifest field and an ADR of its own), not before.

## Alternatives

- **Percentile winsorising (1%/99%).** On 30 names it clips one name at each end
  every period whatever the data. Rejected for MAD.
- **Rank transform instead of z-score.** Rank IC already uses ranks; Pearson IC
  on ranks would only duplicate it. Rejected; the z-score is what Pearson IC
  and spreads on a linear scale need.
- **Fit over the pooled panel.** Leaks the dispersion of later sessions.
  Rejected.
- **Sector-neutral on today's taxonomy.** Look-ahead by construction. Rejected
  for v1.

## Trade-offs

- **No sector control.** A factor that only sorts sectors (value often does)
  shows up as a factor. The report says the result is not sector-neutral.
- **Fixed constants.** 3 MAD and 5 names are a choice, not fitted. Changing
  them is a new version and a new trial.

## Consequences

- `personal_quant.factors.transforms` with `winsorise_zscore` and `identity`,
  each with its version string.
- Tests (from QA + QC's plan): a hand-computed 5-name case to 1e-12; a constant
  cross-section gives empty; an absent input stays absent; fewer than 5 names
  gives empty; input order does not matter; changing a value at another session
  does not change the output at this one.
