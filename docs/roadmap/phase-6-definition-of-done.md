# Phase 6 — definition of done (Sprint 7, item 7.4)

Status: evidence filled by Lead on 2026-10-21; Sprint 7 closed by the Moderator the same day. Open items go to the next sprint (backlog, ACB capital step) and to a later UI/UX phase (flaky frontend test). Each criterion names the evidence that closes it.
Evidence is filled in at the end of Sprint 7; a criterion with no evidence is
not met. The operator declares Phase 6 complete; this document does not.

## Criteria

| # | Criterion | Evidence required | Evidence |
| --- | --- | --- | --- |
| 1 | Filings are held for the universe, with gaps named | Count of held filings per ticker from `data/fundamentals/ids`; STB and GVR listed as not collected, HPG ×2 and VNM 2025 Q2/H1 as missing periods | 1,602 filings held for 33 tickers (`data/fundamentals/ids`, 2026-10-21). `pqt coverage`: STB and GVR "not collected" (IDS fails on every filing page); HPG held, 52 reports, 2021Q4 → 2026Q2; FPT 58. HPG ×2 (consolidated Q2 and H1 2022) and VNM 2025 Q2/H1 are recorded as missing periods in ADR-029 and the backlog below. |
| 2 | Statements are read as of a date | Reads for 3 real names at 2 dates each; a report filed after the date does not change the value (synthetic restatement test plus one real before/after pair) | FPT: session 2023-07-19 reads 2023Q1 (P/E 17.84), 2023-07-20 reads 2023Q2 (P/E 17.56), the Q2 report being known 2023-07-19 17:00 UTC (`evidence/sprint-7-c-dataset.md`; reproduced by Lead with `pqt fundamentals ratios`). Synthetic restatement tests in `FundamentalRatioServiceTests`. Also VCB and ACB (Lead, `pqt fundamentals ratios --on`, 2023-07-14…08-03): both read 2023Q1 up to 2023-07-28 and 2023Q2 from 2023-07-29 (VCB P/E 16.05 on Q1, absent on Q2; ACB P/E absent on both). Three real names, each with a before/after switch. |
| 3 | Ratios follow ADR-030 | Hand-computed P/E, P/B, ROE, EPS, dividend yield for one real name; share-count checks and findings shown for ACB (2023 Q2 capital step) | FPT 2023-08-01 by hand: P/E 18.48, P/B 4.44, ROE 26.58%, yield 1.18% (service, CLI, API and export agree). **Reworded:** ACB 2023 Q2 capital step (+15.0%) is visible in `shares` (3,377,435,100 → 3,884,050,400) but carries NO finding (0 of 378 rows), because ADR-030 checks only a step near an action. It stays in the backlog; the check for a step with no action is not built. |
| 4 | Every absent ratio says why | Absence codes (`RatioAbsence`) listed for a name with no balance sheet, a trailing loss, a missing quarter, no dividend coverage | Codes over the 9,828-row export: `no_dividend_coverage` 8,788; `missing_quarter` 3,724 (HPG 2024Q3 gap, ACB 2022 forms); `trailing_loss` 212; `equity_not_known_at_both_ends` 209; `no_charter_capital` 185. |
| 5 | Surfaces show it | Console `FA`, `GET /instruments/{id}/ratios` and `/fundamentals` return the same numbers as the service for the same name and date; coverage row shows STB and GVR as not collected | `pqt fundamentals ratios FPT --on 2023-08-01` and `GET /instruments/{id}/ratios?on=2023-08-01` return the same numbers (EPS 4,573.37; P/E 18.4765; P/B 4.4435); `/fundamentals` returns 200; `pqt coverage STB|GVR` shows not collected. Console `FA` is covered by the frontend tests, not re-run by hand. |
| 6 | The dataset carries it (ADR-031) | Export of 3 names with `--fundamentals`; manifest v4 verifies in .NET and Python; v1–v3 manifests still verify; Python reader refuses the five cases in ADR-031 §7 | Dataset eee61890eba90fa9 (v4, 9,828 rows, 26 names × 378 sessions, 4 not collected): `dataset verify` passes in .NET and Python. v1–v4 fixture hashes agree in both languages. ADR-031 §7 refusals map to named tests (`evidence/sprint-7-c-dataset.md`). Window is 2023-07-03 → 2024-12-31 with `as_of 2024-08-05` (survivorship caveat stated there); a point-in-time export over 2023 is refused because VN30 coverage starts 2024-08-05. |
| 7 | No leak | As-of test over every column in `fundamentals.parquet`: a value at session D uses nothing published after D or after the dataset `known_as_of` | `check_fundamentals_asof.py` over all 9,828 rows: 0 failures; negative control (reports shifted +30 days) gives 9,851 failures. |
| 8 | Suites green | `dotnet test` unit suite, Python tests, frontend tests, integration tests in Docker: counts and run date | 2026-10-21 at `985ba9e`: .NET unit 1,249/1,249; Python 280 passed, 25 skipped (Qlib optional); integration suite 206/206 (Docker, 8 min); frontend 305/306. The one failure is `Console.test.tsx` "echoes an operator key masked…", a 5,000 ms timeout under load; an earlier run this session also showed environment errors. **Known flaky; not fixed.** |
| 9 | Backlog listed, not hidden | The list below is in `handoff.md` with an owner or "later" for each item | Listed below. Owner or "later" per item: set by the Moderator (default: later). |

## Known limits that do not block Phase 6

- Scope: banks and non-financials only in the first import; SSI, TCX, BVH are out of scope (`out_of_scope`).
- Eight tickers (BCM, MSN, MWG, PLX, POW, VNM, VPL, VRE) have their corporate
  actions swept by hand from VSDC notices from 2023-07 (VPL from 2025-05-13;
  sprint 8.4, ADR-032). Dividend yield is present only inside such a sweep;
  every other name carries `dividend_history_incomplete`. The 365-day proxy is
  removed.
- VPL has filings only from 2025.

## Backlog (after sprint 8)

Sprint 7's list, updated by sprint 8 (`evidence/sprint-8*.md`). `handoff.md`
is git-ignored, so this list is the record. Owner is "later" unless the
Moderator names one.

| Item | Source | Owner |
| --- | --- | --- |
| STB and GVR not collected (IDS filing page fault) | ADR-029 note 2026-10-21 | later |
| VNM 2025 Q2 and H1 not listed by IDS | ADR-029 note 2026-10-21 | later |
| LPB: 11 unclassified quarterlies (2021Q4 → 2024Q2) stay refused | sprint-8-3.md §2 | later |
| P1: GAS income lines shifted on five forms; profit 61 read as a known 0 | sprint-8-3.md §5 | sprint 9 (proposed) |
| P2: VPB 2025Q4 form recorded as 2024Q4; a `PeriodConflict` rule | sprint-8-3.md §4 | sprint 9 (proposed) |
| P3: ACB 2023 H1 separate form recorded as consolidated; rule 211 > 0 | sprint-8-3.md §3 | sprint 9 (proposed) |
| P4: separate reports with consolidated-only lines and no twin (MBB 2021Q4, POW, VCB, VIC 2022, ACB 2024 annual) | sprint-8-3.md §3 | sprint 9 (proposed) |
| ACB bars before 2023-07-03, so the 2023-06-01 actions do not adjust | sprint-8.md §8.1 | later |
| Dropped EPS lines that are not ×10^6 typos: VRE 2 (about 2× EPS), SSB 1 | sprint-8-3.md | later |
| MBB two capital values (repeated form); ACB 2022 Q1–Q3 forms with no values | sprint 7 backlog | later |
| Flaky `Console.test.tsx` | DoD #8 | UI/UX phase |

Closed in sprint 8: the ACB/SHB/SSB/VIB shared-form check (SHB, SSB, VIB are
consolidated; ACB is P3); the 22 refusals (confirmed correct); the 111 dropped
EPS lines (88 ×10^6 typos, 20 GAS money values, 3 above); HPG ×2 collected;
ACB and HPG capital steps (rows added). The detector's ex-date window
(HPG 2025Q3, TPB 2024Q4) is fixed in sprint 8 if the Moderator approves it,
otherwise it is listed here.

## Closing

At the end of Sprint 7 Lead fills the Evidence column from real runs (commands
and outputs kept under `docs/roadmap/evidence/`), the Moderator reviews, and the
operator declares Phase 6 complete or lists what is missing.
