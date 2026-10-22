# ADR-033: The feature catalogue — what a feature is, and the first eight

**Status:** Proposed · **Date:** 2026-10-21 · **Phase:** Sprint 8 item 8.5, built in sprint 9 (Phase 7)

## Context

Phase 7 turns the research layer from one vertical slice (the 12-1 momentum
factor, U6) into a catalogue that factor reports can evaluate. Three things
already exist: the `FeatureEngine` and `FactorEngine` protocols, each with a
`name`, a `version` and a declared `Lookback`; the point-in-time harness, which
hands an engine a `History` cut at the as-of session and no deeper than its
lookback (it raises on look-ahead, over-reach and a non-member score); and, since
Sprint 7, `fundamentals.parquet` in the dataset (ADR-031) with ratios computed
once, by the backend (ADR-030, ADR-032).

What is missing is a decision on what a catalogue entry must state, how a
feature reads fundamentals without opening a second path to the data, and which
features come first. The limit of the sample shapes the answer: VN30
membership is recorded from 2024-08-05 and stored bars from 2023-07-03, so the
usable window is about 2024-08 → now (roughly 26 monthly rebalances to
2026-10). The sprint 8 dataset `e1d22bee4937dad9` ends 2025-12-31, which gives
about 17; sprints 9 and 10 run on a build that ends at the latest stored bar,
and each report states the count its build gives (§5). The
Moderator kept VN30 as it is (2026-10-21), so the window is a stated limit of
every feature and report, not something sprint 9 widens.

## Decision

**A feature is an entry that declares what it reads, how far back, what makes
it absent and which way it points, and the harness checks the first two.**

### 1. What an entry declares

Every catalogue entry is a `FeatureEngine` (a `FactorEngine` where it is
cross-sectional, as momentum is) and states:

- `name` and `version`; the version changes with the computation, and both are
  recorded with every run (U9).
- `lookback`, in market sessions of the dataset, counting the as-of session.
- `inputs`: the dataset columns it reads (`close`, `turnover`, and the
  fundamentals columns by name).
- `absence`: the rule that leaves an instrument out of the result, in one
  sentence. **Absent stays absent: an instrument without a value is not in the
  mapping, never zero, never filled forward.** The reason, where there is one,
  is the dataset's own `_absent` code.
- `direction`: informational (`higher_is_larger_value`, etc.), not applied. A
  feature is stored in its natural sign; flipping a sign belongs to the
  evaluation (sprint 10), where it is recorded, not hidden in the feature.

### 2. Fundamentals reach a feature through the dataset only

`History` gains one read: `fundamentals(instrument_id) -> FundamentalsRow | None`,
the row of `fundamentals.parquet` **at the as-of session**, with its `_absent`
codes. There is no read of another session, so a fundamentals feature has a
lookback of 1 and cannot look ahead by construction; the harness still checks it
(the planted over-reach test is repeated for a feature that tries a later
session).

- A dataset without `--fundamentals` makes a fundamentals feature refuse to
  run, with the reason, rather than return an empty cross-section.
- `ratio_version` below 2 is refused by the value features: the dividend and
  capital-step rules of ADR-032 are what make P/E-based values worth reading.
- Features never compute a ratio themselves. They divide two values the service
  already produced, so there is one share count and one TTM rule.
- Fundamentals rows exist only for membership sessions (ADR-031), which is also
  where a feature is evaluated.

### 3. Catalogue v1

| Feature | Reads | Definition | Absent when |
| --- | --- | --- | --- |
| `momentum_12_1` | adjusted close | return from 252 to 21 sessions ago (exists, v1; lookback 253 sessions with the as-of) | no close 252 sessions ago or 21 sessions ago, or a start close ≤ 0 |
| `momentum_6_1` | adjusted close | return from 126 to 21 sessions ago (lookback 127) | no close 126 sessions ago or 21 sessions ago, or a start close ≤ 0 |
| `volatility_60` | adjusted close | standard deviation of daily log returns over 60 sessions | a missing close in the window |
| `liquidity_20` | turnover | median over 20 sessions of the traded value (VND) of the bar | a missing bar in the window |
| `size` | `shares`, `close_raw` | shares × raw close (market capitalisation) | either is absent |
| `value_ep` | `eps_ttm`, `close_raw` | EPS (TTM) ÷ raw close, so a loss is a negative number, not a missing one | EPS or close absent |
| `value_bp` | `book_value_per_share`, `close_raw` | book value per share ÷ raw close, so negative equity is a negative number, not a missing one | book value or close absent (`no_balance_sheet`, `no_charter_capital`, `no_close`); `equity_not_positive` belongs to `pb_absent` only and is never cited for this feature |
| `quality_roe` | `roe` | ROE (TTM) as the dataset states it | `roe` absent |

That is eight entries: seven new, and momentum 12-1, which was already there.

Decisions inside the table:

- **E/P and B/P, not P/E and P/B.** A multiple is absent for a loss and explodes
  near zero earnings; its inverse is defined and ordered. P/E and P/B stay in
  the dataset for people; features use their inverses.
- **Raw close with ratios, adjusted close with returns.** The ratios in the
  dataset are on the raw close and today's share basis (ADR-030); a price
  feature uses the dataset's adjusted series. A feature never mixes the two
  bases in one expression.
- **Dividend yield is not a feature in v1.** Only eight names carry a coverage
  record (ADR-032), so it is absent almost everywhere.
- **`liquidity_20` reads turnover.** Checked 2026-10-21 on FPT across its 1.15
  bonus: the dataset's `volume` is adjusted by the cumulative share factor like
  the close (raw 1,328,101 → 2,510,220 on 2023-07-04 → 07-05; dataset
  2,019,876 → 3,319,766), so it is consistent within a build. Turnover (VND
  traded) is basis-free and comparable across names, so the feature reads it.
  It is in `bars.parquet` but not yet in the Python `Bar` or the DuckDB `bars`
  view; adding it is a small reader change in sprint 9.

### 4. How a feature is shown to be point-in-time

For every entry, a test holds all of:

- a planted over-reaching twin is caught by the harness (lookback exceeded or
  look-ahead), as the U6 test does for momentum;
- a hand-computed value on a synthetic dataset;
- the absence rule: a missing input leaves the instrument out, and a synthetic
  zero is not substituted;
- for a fundamentals feature, a restatement after the as-of changes the value
  only for sessions after it (the dataset test of Sprint 7, repeated at the
  feature level).

### 5. The sample-size statement

Each factor report states the window it ran on (first and last scored session,
number of rebalances, number of names per rebalance) and says that the sample is
at most about 26 monthly cross-sections of at most 30 names (fewer on a build
that ends earlier, such as about 17 for `e1d22bee4937dad9`). A result on that sample is
described as exploratory. The report does not hide the window behind a
significance number.

## Alternatives

- **Features compute ratios from statement lines.** A second implementation of
  ADR-030 in Python. Rejected for the reason ADR-031 gives.
- **A free-form `compute(history)` with no declared inputs.** Nothing to check
  against, and the catalogue cannot say what a run depended on. Rejected.
- **Return a zero for an absent value.** A zero ranks as a real number in a
  cross-section; a loss-making name and an unknown name look the same.
  Rejected, as in ADR-030.
- **P/E and P/B as features.** Absent for losses, unbounded near zero. Rejected
  in favour of E/P and B/P.
- **Sector-neutral and winsorised in the feature.** Cross-sectional transforms
  belong to the evaluation (sprint 10), versioned there, so a feature stays a
  raw, reproducible quantity.

## Trade-offs

- **Eight features on a thin sample.** The catalogue is wider than the data can
  separate, so correlations between factors in sprint 10 will be noisy. The
  catalogue is cheap to compute and each entry is a hypothesis, counted in
  `trial_count`, not a finding.
- **Survivorship in the window.** Membership is as recorded from 2024-08-05;
  the dataset states `membership: point-in-time` only where coverage allows.
- **Eight names have thin fundamentals.** Their yield is absent and their share
  counts rest on newly transcribed actions (ADR-032); `size`, `value_ep` and
  `value_bp` for those names carry that.
- **Value and size features lean on ADR-030's treasury-share bias**, which
  overstates P/E, P/B and market capitalisation (so understates E/P and B/P and
  overstates `size`) for an issuer with treasury shares.

## Consequences

- `History` gains `fundamentals(...)`; the Python dataset reader already exposes
  the `fundamentals` view, so no new file is read.
- The catalogue lives in `personal_quant.factors`, one module per family,
  with a registry that lists name, version, inputs, lookback and absence rule so
  a run record can name every feature it used.
- Sprint 9 builds this after ADR-032's code (`ratio_version` 2) has landed;
  features read the dataset's version and refuse version 1 for value.
- Sprint 10 adds the transforms and the evaluation on top; nothing here changes
  when those arrive.
- `History.fundamentals` does not copy report ids into the run record: the run
  pins the dataset id and content hash, the hash pins `fundamentals.parquet`,
  and its rows carry `report_ids` (Dev, 2026-10-21).
