# Sprints 8–10 plan — DRAFT v3, not approved

Requested by the Moderator on 2026-10-21. Lead and Dev planned it together
(Dev's draft is the base for sprints 8–10; Lead edited). It needs a "yes" from
Lead, Dev and the Moderator before any code. Phases and scope belong to
`pqt-roadmap-v2.md`; this reorders `sprint-plan.md`, which had Phase 7 in
sprints 8–9.

## Why this order

- The Moderator moved the Phase 6 backlog and ACB's capital step into the next
  sprint, so sprint 8 is a closing sprint for Phase 6.
- Research reach is the binding constraint: VN30 membership exists from
  2024-08-05 and bars from 2023-07-03, so a point-in-time window is about
  1.4 years. Phase 7 results on that are thin. Widening the window is therefore
  first in sprint 9, before factors are judged.

| Sprint | Theme | Exit |
| --- | --- | --- |
| 8 | Close Phase 6 | Backlog done or re-decided; DoD re-run; operator declares Phase 6 complete or lists what is left |
| 9 | Longer window + Phase 7 foundations | Window stated and widened where entitlements allow; labelled-dataset spec resolves to a manifest; first four factors registered |
| 10 | Phase 7 evaluation | Factor reports reproducible, counted in `trial_count`; Phase 7 definition of done |

## Sprint 8 — close Phase 6

- **8.1 Capital step with no action (ADR-030 amendment).** A check that
  reports a charter-capital step over 2% with no announced action near it, as
  a finding only (no share change). Cases: ACB 2023Q2 (+15%), MBB 2022Q4/2023Q4.
  Findings travel in the dataset, so `ratio_version` becomes 2; v4 datasets at
  version 1 still verify and say which version they carry.
- **8.2 IDS capture gaps.** Retry HPG ×2 (wider search dates) and VNM 2025
  Q2/H1; one more attempt at STB and GVR. Anything still missing is named in
  ADR-029 with the reason.
- **8.3 Scope and refusals.** PDF spot check of the shared form for ACB, SHB,
  SSB, VIB; triage of the 22 refused filings; review of the 111 dropped EPS
  lines (confirm ×10^6 typos).
- **8.4 Corporate-action coverage (Dev found it; decision (a) below).** The
  75 rows in `data/fixtures/corporate-actions.csv` were sourced in sprint 5 to
  explain price-band breaches, so cash dividends of 1–5% (which never breach
  ±7%) are mostly missing: FPT 3 since 2016, GAS 1, SAB 0, VCB none. Today's
  `dividend_yield` is therefore mostly understated (FPT 2024-12: 0.70%), and
  the 365-day coverage proxy is wrong in principle. Share-count actions are
  much better covered (bonus and stock dividends of 5% or more breach the
  band; ESOP of about 1% is absorbed by the 2% tolerance). Options:
  - **Eight names only** (BCM, MSN, MWG, PLX, POW, VNM, VPL, VRE), 2023-07 →
    now, about 30–50 VSD notices by hand: one item.
  - **Full cash-dividend sweep, 26 names:** about 150–250 notices; a sprint-sized
    data task. Records per name `complete_from`, `swept_on` and source in a
    fixture (`corporate-action-coverage.csv`); yield is present only when
    t − 365 ≥ `complete_from`.
  - **Neither:** `dividend_yield` is absent for every name with a new code
    (`dividend_history_incomplete`).
  Whichever is chosen is part of ADR-030 v2.
- **8.5 Feature ADR (Lead).** Written in sprint 8; no feature code in sprint 8.
  `ratio_version` 2 (8.1 and 8.4 together, bumped once) lands before the
  catalogue is frozen, because value features read P/E and P/B.
- **8.6 Close.** Re-run the Phase 6 definition of done.

Owners: Dev code and data runs; Lead ADR amendments, review, DoD. The collector
rules stay (pause 3, at most two collectors, no code edits during a collection).

## Sprint 9 — longer window and Phase 7 foundations

- **9.1 Longer point-in-time window.** VN30 membership before 2024-08-05 from
  HOSE review announcements (2022 → 2024) and a bar backfill 2022-01 →
  2023-07, with entitlements checked first. About 1.4 years becomes about 3.5.
  If a source is not licensable, the window stays and the limit is stated.
- **9.2 Declarative labelled dataset (ADR, Lead).** Request spec: universe,
  as-of, window, frequency, features with versions, forward-return label
  (horizon, close t → close t+h, raw or adjusted). Resolves to a manifest
  extension (v5), not a parallel dataset concept.
- **9.3 Feature catalogue v1 code on the U6 protocols (Dev).** First factors:
  momentum 12-1, value (E/P, B/P from the fundamentals export), quality (ROE),
  size (shares × close). Each declares its lookback and an absence rule; absent
  stays absent, never zero; the point-in-time harness checks each.

## Sprint 10 — Phase 7 evaluation

- **10.1 Cross-sectional transforms:** winsorise, z-score, optional sector
  neutral; versioned.
- **10.2 Factor evaluation as worker jobs:** IC and rank-IC series, decay over
  horizons, quantile spreads, turnover, factor correlation. Every run is
  recorded in the U9 experiment store with `trial_count`.
- **10.3 Phase 7 definition of done** and a first research report on VN30
  factors, with the sample-size caveat stated.
- **Stretch (decide at the start of sprint 10):** the console `RUNS` view and
  the probability-calibration and expected-value substrate. **Alpha158 and the
  Qlib run mapper are optional** (they depend on the optional Qlib environment;
  the native factors carry the Phase 7 claims): sprint 10 if time allows, else
  later. The roadmap assigns calibration/EV and Alpha158 to Phase 7, so if they
  do not fit, Phase 7 closes in sprint 11 and Phase 8 shifts one sprint. That
  is stated here rather than hidden.

Phase 8 (Vietnamese-rules backtest, then validation) starts after Phase 7.

## Not in these sprints

UI/UX phase and the flaky `Console.test.tsx` (Moderator: a later dedicated
phase). Proposal for the Moderator: place it after Phase 7, before Phase 8, when
the factor views exist. VN100 and new data sources beyond 9.1.

## Working rules (unchanged)

Sprint ends green (all suites, reviews, docs); real data never committed;
commits carry no Co-Authored-By; push and merge `main` only on the Moderator's
say-so; the operator declares a phase complete.

## Questions for the Moderator

1. **Corporate actions (8.4):** the eight names only, the full cash-dividend
   sweep for 26 names, or neither (dividend yield absent everywhere)? Lead and
   Dev recommend the eight names plus a sweep only if the Moderator wants
   dividend yield as a factor input.
2. Declare Phase 6 complete at the end of sprint 8 (this plan) or now, with the
   backlog carried?
3. (Dev's question (b).) Widen VN30 history, or state the narrow window
   (about 2024-08 → now, roughly 26 monthly rebalances) as a limit? Is sourcing VN30 membership before 2024-08 and bars before 2023-07 (9.1)
   acceptable in effort and licence? It decides how meaningful Phase 7 is.
4. Where does the UI/UX phase go (proposal above)?

## Agreement

- [x] Lead
- [x] Dev (ticked with edits, which v3 applies; to confirm v3)
- [ ] Moderator
