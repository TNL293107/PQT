# VN30 factor report — Phase 7 (first look, exploratory)

Written by QA + QC on 2026-10-28 for Phase 7 DoD criterion 7. Every number below
is rendered by a script from one read-only query of the run store
(`tmp/phase-7-report-runs.sql`, `BEGIN READ ONLY … ROLLBACK`, output
`tmp/phase-7-eval/report-runs.txt`). None was typed by hand. Tester + Audit gated
the runs first (`tmp/audit-phase-7-runs/verdict.txt`, PASS) and re-derived 24 of
the values by hand, equal to within 1e-12.

> Window 2024-08-05 to 2026-07-20: 24 rebalances, 30-30 names (median 30). The
> sample is at most about 26 monthly cross-sections of at most 30 names; results
> on it are exploratory (ADR-033 §5).

## How to read the numbers

**Fourteen configurations were tried, so `trial_count` = 14 for every number in
this report.** The t-statistics are printed beside the means, but **no t here is a
significance test.** Each t ignores the other 13 trials, and with 14 trials a
|t| above 2 somewhere is expected by chance. Five t values in the tables have
|t| above 2: size's rank IC under both transforms and its IC under `wz-1`, and
quality_roe's spread under both transforms. They are read under the same rule as
every other number. Factors are
listed in catalogue order, not ranked, and this report does not call any of them
better than another.

## The data and the runs

| | |
| --- | --- |
| Dataset | `0e7d28336af399e1` v1, schema 5, content hash `f5c7793afe64f52e7b84b9c4dfc12cc228ebc0c848c25ff036e5c698fed69ec4` |
| Request | `tmp/requests/vn30-sprint-9-1.json` (ADR-034): all 8 catalogue features, rebalance every 21 sessions, label close t to close t+21, adjusted |
| GAS | The build was made **after** the GAS P1 re-import (`evidence/sprint-9-gas-p1.md`) |
| Experiment | `phase-7-vn30`: 15 runs, made of 14 trials and 1 reproduction; latest `trial_count` 14; 0 failed |
| Configurations | 7 features × {`raw`, `wz-1`} (ADR-035); quantiles 5; decay horizons 5, 10, 21, 42, 63 |
| Correlation | `phase-7-vn30-correlation`, its own experiment, so it is not one of the 14 |
| Reproduction | `4c7e5080-f226-4dac-a540-5bf0119e3a07` reproduces trial 1 (`ae6813d6-…`): status succeeded, all 29 metrics equal (Tester `repro.txt`) |
| Code | every run at commit `2faf336` |
| How run | Dev called the handlers directly from the host, so `job_id` is NULL; the backend cannot queue `factor_evaluation` yet. The Moderator approved the live write directly in Dev's pane. |

`liquidity_20` is not evaluated (Moderator, 2026-10-28; see Known limits).

## Per feature

"names (mean)" is the mean number of names with a feature value per rebalance,
out of 30. Turnover has 23 periods because the first rebalance has no previous
holding.

### `raw`

| trial | feature | names (mean) | rank IC mean | t | IC mean | t | spread mean | t | turnover mean | periods (IC / spread / turnover) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | momentum_12_1 | 29.9 | +0.0784 | +1.30 | +0.1107 | +1.56 | +0.0177 | +0.99 | 0.1986 | 24 / 24 / 23 |
| 3 | momentum_6_1 | 30.0 | +0.0425 | +0.76 | +0.0927 | +1.42 | +0.0104 | +0.74 | 0.3333 | 24 / 24 / 23 |
| 5 | volatility_60 | 28.5 | -0.0564 | -0.90 | -0.0537 | -0.76 | -0.0087 | -0.61 | 0.3036 | 24 / 24 / 23 |
| 7 | size | 26.4 | +0.1083 | +2.07 | +0.1191 | +1.97 | +0.0228 | +1.32 | 0.0609 | 24 / 24 / 23 |
| 9 | value_ep | 21.7 | +0.0571 | +1.02 | +0.0486 | +0.86 | +0.0066 | +0.35 | 0.1341 | 24 / 24 / 23 |
| 11 | value_bp | 26.4 | +0.0800 | +1.37 | +0.0888 | +1.49 | +0.0174 | +1.08 | 0.1739 | 24 / 24 / 23 |
| 13 | quality_roe | 20.6 | -0.0724 | -1.16 | -0.1038 | -1.58 | -0.0438 | -2.58 | 0.0906 | 24 / 24 / 23 |

### `wz-1`

| trial | feature | names (mean) | rank IC mean | t | IC mean | t | spread mean | t | turnover mean | periods (IC / spread / turnover) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2 | momentum_12_1 | 29.9 | +0.0777 | +1.29 | +0.0936 | +1.31 | +0.0177 | +0.99 | 0.1986 | 24 / 24 / 23 |
| 4 | momentum_6_1 | 30.0 | +0.0399 | +0.71 | +0.0611 | +0.97 | +0.0104 | +0.74 | 0.3333 | 24 / 24 / 23 |
| 6 | volatility_60 | 28.5 | -0.0567 | -0.90 | -0.0588 | -0.85 | -0.0087 | -0.61 | 0.3036 | 24 / 24 / 23 |
| 8 | size | 26.4 | +0.1083 | +2.05 | +0.1204 | +2.04 | +0.0228 | +1.32 | 0.0609 | 24 / 24 / 23 |
| 10 | value_ep | 21.7 | +0.0567 | +1.01 | +0.0486 | +0.86 | +0.0066 | +0.35 | 0.1341 | 24 / 24 / 23 |
| 12 | value_bp | 26.4 | +0.0800 | +1.37 | +0.0888 | +1.49 | +0.0174 | +1.08 | 0.1739 | 24 / 24 / 23 |
| 14 | quality_roe | 20.6 | -0.0742 | -1.19 | -0.1063 | -1.62 | -0.0438 | -2.58 | 0.0906 | 24 / 24 / 23 |

## Decay: mean rank IC (periods)


### `raw`

| feature | h=5 | h=10 | h=21 | h=42 | h=63 |
| --- | --- | --- | --- | --- | --- |
| momentum_12_1 | +0.0071 (24) | -0.0079 (24) | +0.0784 (24) | +0.0708 (23) | +0.0819 (22) |
| momentum_6_1 | +0.0329 (24) | -0.0440 (24) | +0.0425 (24) | +0.0790 (23) | +0.0866 (22) |
| volatility_60 | -0.0670 (24) | +0.0317 (24) | -0.0564 (24) | -0.1017 (23) | -0.0913 (22) |
| size | +0.0318 (24) | +0.0303 (24) | +0.1083 (24) | +0.0940 (23) | +0.1178 (22) |
| value_ep | +0.0584 (24) | +0.0988 (24) | +0.0571 (24) | +0.1111 (23) | +0.1186 (22) |
| value_bp | +0.0541 (24) | +0.0765 (24) | +0.0800 (24) | +0.1223 (23) | +0.1443 (22) |
| quality_roe | -0.0785 (24) | -0.0325 (24) | -0.0724 (24) | -0.0716 (23) | -0.1401 (22) |

### `wz-1`

| feature | h=5 | h=10 | h=21 | h=42 | h=63 |
| --- | --- | --- | --- | --- | --- |
| momentum_12_1 | +0.0062 (24) | -0.0089 (24) | +0.0777 (24) | +0.0695 (23) | +0.0812 (22) |
| momentum_6_1 | +0.0311 (24) | -0.0473 (24) | +0.0399 (24) | +0.0756 (23) | +0.0823 (22) |
| volatility_60 | -0.0683 (24) | +0.0302 (24) | -0.0567 (24) | -0.1024 (23) | -0.0907 (22) |
| size | +0.0312 (24) | +0.0305 (24) | +0.1083 (24) | +0.0928 (23) | +0.1177 (22) |
| value_ep | +0.0582 (24) | +0.0988 (24) | +0.0567 (24) | +0.1108 (23) | +0.1180 (22) |
| value_bp | +0.0541 (24) | +0.0765 (24) | +0.0800 (24) | +0.1223 (23) | +0.1443 (22) |
| quality_roe | -0.0809 (24) | -0.0351 (24) | -0.0742 (24) | -0.0741 (23) | -0.1425 (22) |

Decay at h = 21 equals the main rank IC exactly, as the acceptance test requires.
**There is no t-statistic for h > 21** (ADR-036 §3): the labels overlap, so
their ICs are autocorrelated. A rebalance whose t + h passes the last session is
dropped, which is why h = 42 has 23 periods and h = 63 has 22.

## Correlation between features

The mean of the per-rebalance Spearman correlations under `wz-1` (not pooled), with its period count, in name order.

| pair | mean | periods |
| --- | --- | --- |
| liquidity_20 / momentum_12_1 | absent | 0 |
| liquidity_20 / momentum_6_1 | absent | 0 |
| liquidity_20 / quality_roe | absent | 0 |
| liquidity_20 / size | absent | 0 |
| liquidity_20 / value_bp | absent | 0 |
| liquidity_20 / value_ep | absent | 0 |
| liquidity_20 / volatility_60 | absent | 0 |
| momentum_12_1 / momentum_6_1 | +0.6463 | 24 |
| momentum_12_1 / quality_roe | -0.0702 | 24 |
| momentum_12_1 / size | +0.3072 | 24 |
| momentum_12_1 / value_bp | +0.0013 | 24 |
| momentum_12_1 / value_ep | -0.0437 | 24 |
| momentum_12_1 / volatility_60 | +0.3543 | 24 |
| momentum_6_1 / quality_roe | -0.1099 | 24 |
| momentum_6_1 / size | +0.2755 | 24 |
| momentum_6_1 / value_bp | -0.0716 | 24 |
| momentum_6_1 / value_ep | -0.1108 | 24 |
| momentum_6_1 / volatility_60 | +0.3594 | 24 |
| quality_roe / size | +0.1592 | 24 |
| quality_roe / value_bp | -0.0513 | 24 |
| quality_roe / value_ep | +0.4809 | 24 |
| quality_roe / volatility_60 | -0.2591 | 24 |
| size / value_bp | -0.2104 | 24 |
| size / value_ep | -0.1136 | 24 |
| size / volatility_60 | +0.0141 | 24 |
| value_bp / value_ep | +0.7718 | 24 |
| value_bp / volatility_60 | -0.1109 | 24 |
| value_ep / volatility_60 | -0.2295 | 24 |

The `liquidity_20` pairs are absent, with 0 periods, because the feature has no
value at any rebalance.

## Known limits

- **The sample is small.** 24 rebalances of at most 30 names, so every result is
  exploratory.
- **`liquidity_20` is out** (Moderator, 2026-10-28). CafeF reports no turnover,
  0 of 22,545 daily bars since 2024. The 9.1 request still names it, so its 7
  correlation pairs come out absent.
- **GAS:** `value_ep` and `quality_roe` are absent for GAS at every session.
  GAS has no 2025Q1–Q3 forms, and its 2026 forms are Circular 99 with no values.
  A quarter's profit is derived from year to date, so 2025Q4 breaks too.
- **Circular 99:** no name's fiscal-2026 values are read yet. That cuts the
  value and quality coverage late in the window.
- **Coverage is under 30.** Value and quality features cover about 21–26 names
  per rebalance on average (table above), so they are not compared on the same
  names as the price features.
- **No sector-neutral transform.** The dataset carries no point-in-time sector
  (ADR-035 §3), so a factor that only sorts sectors shows up as a factor.
- **No decay t-statistic beyond the rebalance horizon** (ADR-036 §3).
- **Treasury-share bias** understates E/P and B/P and overstates size
  (ADR-030). The share counts of eight names rest on 24 VSDC rows read by hand
  (ADR-032).
- **Frictionless.** Returns are close to close, with no costs, price bands, lot
  sizes or T+2.5. This is not a backtest; that is Phase 8.
- **No multiple-testing adjustment** is applied here (Phase 8 brings the
  Deflated Sharpe Ratio and PBO on top of `trial_count`).

## Reproduce

Any run below can be re-executed with `reproduce_run` (payload `{"run_id": …}`).
The tables are rendered by `tmp/phase-7-eval/qa/tables.py` from
`tmp/phase-7-eval/report-runs.txt`; `tmp/` is git-ignored.

### Run ids

| trial | feature | transform | run id | status | commit |
| --- | --- | --- | --- | --- | --- |
| 1 | momentum_12_1 | raw | `ae6813d6-1629-4e41-acc2-1f52ccff66c7` | succeeded | `2faf336` |
| 2 | momentum_12_1 | wz-1 | `1bac08e5-6020-4195-b75e-441712470371` | succeeded | `2faf336` |
| 3 | momentum_6_1 | raw | `42730a67-4741-4480-9c01-e98314ca5fea` | succeeded | `2faf336` |
| 4 | momentum_6_1 | wz-1 | `9ce0c133-bb82-47cd-85e3-abf5defe9c24` | succeeded | `2faf336` |
| 5 | volatility_60 | raw | `64f3bd4c-b2a0-43fb-8964-bc76fc4008b1` | succeeded | `2faf336` |
| 6 | volatility_60 | wz-1 | `aee6ae3f-584c-41e6-bff8-15d6dcb64004` | succeeded | `2faf336` |
| 7 | size | raw | `f028f2ab-5b63-4c24-a1c9-f50294a410f4` | succeeded | `2faf336` |
| 8 | size | wz-1 | `b98373e0-8e22-4850-9b50-13efe31f4d92` | succeeded | `2faf336` |
| 9 | value_ep | raw | `0a515470-fb58-440c-b73c-636719c482f2` | succeeded | `2faf336` |
| 10 | value_ep | wz-1 | `6576939b-93a3-4293-ad94-6ea8124aed3c` | succeeded | `2faf336` |
| 11 | value_bp | raw | `ddbd3e64-b69d-413b-8b04-05d9de5ed6e9` | succeeded | `2faf336` |
| 12 | value_bp | wz-1 | `91109634-583c-410f-b2e0-80f39b0ecaa7` | succeeded | `2faf336` |
| 13 | quality_roe | raw | `ac48f3fa-7e90-49a9-87db-81399a45ec33` | succeeded | `2faf336` |
| 14 | quality_roe | wz-1 | `eac2ca71-ccd9-4445-828e-4da5023ee60e` | succeeded | `2faf336` |

Correlation run: `8a50f67c-ddc8-43e5-8171-eb0f51c5d97d`.
