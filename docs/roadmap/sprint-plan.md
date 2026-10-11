# Sprint plan — Sprint 4 onward

**Written 11 October 2026**, after sprint 3. This document schedules work. It
does not redefine it: phases, their numbering and their scope belong to
[`pqt-roadmap-v2.md`](pqt-roadmap-v2.md), and where the two disagree the roadmap
is correct and this plan is a defect.

The plan is detailed for the next four sprints and outlined after that. Each
sprint is re-planned at its start against what the previous one actually
delivered.

---

## Where things stand

| | |
| --- | --- |
| Complete | Phases 0–4; Research Foundation Upgrade (Gates A and B, 2026-10-11) |
| In progress | Phase 5 — every named capability has a first slice (ADR-026, ADR-027) |
| Next on the critical path | Phase 6 → 7 → 8, the chain everything after Phase 8 inherits |

**Real-data reach is the binding constraint, not code.** PQT holds daily CafeF
bars for the VN30 names, VN30 membership from August 2024, and the
entitlements found so far. History before a name's admission is mostly absent,
and the HOSE calendar is not declared before 2022, so missing sessions cannot be
measured for older spans. Phases 6–8 can be built on what exists, but their
results are only as wide as this reach. Sprint 5 exists for that reason.

---

## How every sprint runs

The working rules from sprints 1–3, kept:

- **Work and test in E:\PQT, then fast-forward D:\PQT.** D never gets its own
  commits.
- **A sprint ends green.** Before the sprint closes:
  - .NET unit and integration tests pass, and so do frontend, Python and Qlib.
  - Every change gets a code review, plus a security review where it touches
    a write path, a credential or a boundary.
  - The work is pushed and CI is green, and D is fast-forwarded.
  - The README, the roadmap and `handoff.md` are updated.
- **Every terminal or research capability is an application-layer query**
  reachable from the API, the CLI and the console (ADR-026). Nothing computes
  in a surface what the server could.
- **Real data is never committed.** Exports and CafeF prices stay outside the
  repository.
- **A sprint exercises its work on the live stack**, not only in tests. Sprint 3
  found a real defect that way.
- **Declaring a gate or a phase `COMPLETE` is the operator's call.** A sprint
  ends with the evidence, not the declaration.

---

## Sprint 4 — Close Phase 5

**Goal:** finish what Phase 5's first slices left open, write its definition of
done, and hand the operator the evidence to declare it complete.

| # | Item | Notes |
| --- | --- | --- |
| 4.1 | **Phase 5 definition of done** in the roadmap | Written first, so the rest of the sprint is measured against it |
| 4.2 | **Quotes in watchlists** | Last close, change against the restated previous close, volume, session. Extract the restated-close rule from breadth into one shared application query, `QuoteService`, so breadth and watchlists cannot disagree on a change |
| 4.3 | **Tick-rounded reference prices** | HOSE rounds a stock-dividend reference to the tick (10 / 50 / 100 ₫ by price band). Without it, a name that closes at the reference reads as a small decline |
| 4.4 | **Venue-wide breadth** | `breadth --venue HOSE` over every listed instrument with bars. The point-in-time rule becomes "listed on the session". The answer must state how many listed names have no data at all |
| 4.5 | **HOSE calendar 2016–2021** | Transcribe holidays and closures from HOSE's announcements, so the coverage registry can measure missing sessions on the older FPT span instead of answering *not measured* |
| 4.6 | Small debt | `Experiment` name regex anchors on `$`, the same `\z` defect fixed for watchlists. README "Current work" heading still names the Upgrade |

**Exit:**
- Every Phase 5 DoD criterion is evidenced on the live stack.
- `FPT COV` measures missing sessions back to 2016.
- A watchlist shows prices that agree with `breadth` on the same session.

**Operator decision at close:** declare Phase 5 `COMPLETE`.

---

## Sprint 5 — Data reach

**Goal:** widen real data enough that Phases 6–8 produce results worth reading.
This is not a phase: it serves U3's mandate and the phases that need it.

| # | Item | Notes |
| --- | --- | --- |
| 5.1 | **History backfill, 2023-07 → 2024-08** | Raw CafeF bars for the 30 initial VN30 members and the pre-admission periods of LPB, DGC, VPL, BSR, MCH and TCX |
| 5.2 | **Entitlements for the backfilled span** | Found the way sprint 2 found five: every price-limit breach the quality inspector raises must be explained by a sourced action (VSD notices) or stay open as a finding. Unadjusted history would corrupt 12-1 momentum |
| 5.3 | **Re-export VN30 with `--history 253`** | The momentum slice must score 30 of 30 members at every rebalance (today 28 of 30) |
| 5.4 | **Venue history** | Record HOSE / HNX / UPCOM transfers through the existing transfer service for every name that moved, so a series spanning a transfer stays one instrument |
| 5.5 | **Reproduction check** | Re-run the recorded momentum runs on the new build. The result must differ, and the reproduction must say exactly which metrics moved and why: a new dataset version, not silent drift |

**Exit:**
- The history export covers every member's formation window.
- No unexplained price-limit breach remains in the backfilled span.
- The slice's IC is reported on the wider window, with its t-statistic.

**Risk:** entitlement sourcing is manual and slow. If it runs long, items 5.4
and 5.5 move to sprint 6, and 5.1–5.3 do not.

---

## Sprint 6 — Phase 6, part 1: point-in-time fundamentals

**Goal:** a fundamentals model that cannot leak the future, fed from one real
source.

| # | Item | Notes |
| --- | --- | --- |
| 6.1 | **ADR-028: fundamentals source and licence** | Candidates: issuer filings published through HOSE/HNX disclosure, and aggregators. The binding test is from `system-context.md`: each fact must carry its fiscal period **and** the moment it became public. A source with only the period cannot support honest backtests and is rejected. The licence is checked before any import |
| 6.2 | **Domain model** | A statement line per fiscal period (income, balance sheet, cash flow), with `reported_at` and an append-only revision history like `bar_revisions`. A restatement is a new revision, never an overwrite |
| 6.3 | **Provider and import pipeline** | Same shape as market data: explicit provider selection, raw payload retained, normalisation, findings for implausible values |
| 6.4 | **As-of read** | `knownAsOf` over fundamentals: a statement published after the instant is absent. Tests with a synthetic restatement prove it |
| 6.5 | **Real import** | VN30 names, quarterly, 2022 onward |

**Exit:**
- Real statements are stored with `reported_at`.
- An as-of read provably excludes later publications and later restatements.

---

## Sprint 7 — Phase 6, part 2: ratios and surfaces

| # | Item | Notes |
| --- | --- | --- |
| 7.1 | **Point-in-time ratios** | EPS (trailing twelve months), P/E, P/B, ROE and dividend yield. Each uses the statement known at the instant and the share count adjusted for actions to that instant |
| 7.2 | **Surfaces** | Console `FA` (already reserved as a mnemonic), `pqt fundamentals`, and `GET /instruments/{id}/fundamentals`. The coverage registry's Fundamentals row moves from *not collected* to a measured state |
| 7.3 | **Dataset contract** | The export carries fundamentals as of each session (manifest v4, ADR), so research reads them through the canonical dataset and not a side channel |
| 7.4 | **Phase 6 definition of done** | Written and evidenced |

**Operator decision at close:** declare Phase 6 `COMPLETE`.

---

## Sprints 8–9 — Phase 7: factor research and features

| Sprint | Scope |
| --- | --- |
| 8 | Feature catalogue behind `FeatureEngine`: momentum variants, value (from Phase 6), volatility, liquidity and size. Each is versioned, with its lookback declared and checked by the point-in-time harness. **Declarative labelled-dataset spec** (ADR): universe, as-of, window, features with versions and a forward-return label, resolvable to a manifest. Alpha158 behind `FeatureEngine` through the optional Qlib adapter. A mapper that records Qlib runs in the experiment store with their `trial_count` |
| 9 | Signals and the **probability-calibration and expected-value substrate** the roadmap assigns here. Factor reports run as worker jobs: IC and its decay, turnover, quantile spreads and correlation between factors. A console view of experiments and runs (`RUNS`), reading the same store the CLI does |

**Exit (Phase 7):**
- At least three factor families have been evaluated on the widened VN30 data.
- Every run is reproducible from its manifest, and counted in `trial_count`.

---

## Sprints 10–11 — Phase 8: backtesting and research validation

| Sprint | Scope |
| --- | --- |
| 10 | **Event-driven backtester under Vietnamese market rules**: T+2 settlement, 100-share lots, daily price bands with no fill at a locked ceiling or floor, fees and the 0.1% sell tax, and a participation cap on volume. Every rule is tested against a hand-computed case. It reads only the canonical dataset |
| 11 | **Research validation**, all part of Phase 8's definition of done: walk-forward, out-of-sample, parameter, universe and cost perturbation, regime split, the **Deflated Sharpe Ratio** and **PBO via CSCV** using U9's `trial_count`. Each method must first flag a synthetic, known-overfit control before it is trusted on real results |

**Exit (Phase 8):** a strategy report that states its own probability of being
overfit. This is the milestone the project's research claims rest on.

---

## Beyond sprint 11 — outline

| Order | Phase | First question to settle |
| --- | --- | --- |
| 12–13 | **9 Risk engine** | Ex-ante exposure and ex-post attribution over the Phase 8 portfolio output |
| 14–15 | **10 Portfolio management** | Construction and rebalancing under lot-size and band constraints |
| 16+ | **11 News, events and alternative data** | A Vietnamese-language corpus and entity mapping. The most expensive data phase, deliberately late |
| — | **12 Advanced quant research** | Needs a real strategy library first (see `advanced-research.md`) |
| — | **13–16 Trading** | Paper trading first. `LIVE_TRADING_ENABLED` stays false until Phase 15 |
| — | **17–20** | C++ only after a measured hot path (ADR-005); AI analyst never an order path; authentication in Phase 19 replaces the operator key |

---

## Decisions the operator owns along the way

| When | Decision |
| --- | --- |
| End of sprint 4 | Declare Phase 5 complete |
| Sprint 6 start | Approve the fundamentals source and its licence (ADR-028) |
| End of sprint 7 | Declare Phase 6 complete |
| End of sprint 9 | Declare Phase 7 complete |
| End of sprint 11 | Declare Phase 8 complete |
| Any time | Re-order sprint 5 if a wider universe (VN100) matters more than deeper VN30 history |
