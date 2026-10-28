# Phase 6 — definition of done (Sprint 7, item 7.4)

Status: evidence filled by Lead on 2026-10-21 (sprint 7) and re-run on 2026-10-22 at the end of sprint 8 (item 8.6). Each criterion names the evidence that closes it.
Evidence is filled in at the end of Sprint 7; a criterion with no evidence is
not met. The operator declares Phase 6 complete; this document does not.

## Criteria

| # | Criterion | Evidence required | Evidence |
| --- | --- | --- | --- |
| 1 | Filings are held for the universe, with gaps named | Count of held filings per ticker from `data/fundamentals/ids`; STB and GVR listed as not collected, HPG ×2 and VNM 2025 Q2/H1 as missing periods | **Sprint 8 (2026-10-22):** 1,604 filing documents held for 31 tickers (`data/fundamentals/ids`); the database holds 1,582 IDS reports, 743 with values, 236,398 statement lines; 22 refused, all correct under the rules (`evidence/sprint-8-3.md`); re-counted by Tester + Audit on 2026-10-22 at `962c15e`: the audit mirror gives `filings 1604 refused 22 withheld 692 shared 688 dropped 111`, and its diff with the database is empty. HPG consolidated 2022Q2 and H1 are now held with values; HPG's separate reports of those periods were purged and re-imported without values (`SharedForm`; `evidence/sprint-8-2.md`). Still missing and named in ADR-029 (note 2026-10-21): STB and GVR not collected (IDS filing pages fail), VNM 2025 Q2 and H1 not listed. Sprint 7: 1,602 filings, 2026-10-21. |
| 2 | Statements are read as of a date | Reads for 3 real names at 2 dates each; a report filed after the date does not change the value (synthetic restatement test plus one real before/after pair) | FPT: session 2023-07-19 reads 2023Q1 (P/E 17.84), 2023-07-20 reads 2023Q2 (P/E 17.56), the Q2 report being known 2023-07-19 17:00 UTC (`evidence/sprint-7-c-dataset.md`; reproduced by Lead with `pqt fundamentals ratios`). Synthetic restatement tests in `FundamentalRatioServiceTests`. Also VCB and ACB (Lead, `pqt fundamentals ratios --on`, 2023-07-14…08-03): both read 2023Q1 up to 2023-07-28 and 2023Q2 from 2023-07-29 (VCB P/E 16.05 on Q1, absent on Q2; ACB P/E absent on both). Three real names, each with a before/after switch. |
| 3 | Ratios follow ADR-030 | Hand-computed P/E, P/B, ROE, EPS, dividend yield for one real name; share-count checks and findings shown for ACB (2023 Q2 capital step) | FPT 2023-08-01 by hand: P/E 18.48, P/B 4.44, ROE 26.58%, yield 1.18% (service, CLI, API and export agree), unchanged at ratio version 2 after the HPG re-import (`evidence/sprint-8-2.md`). **Sprint 8 (ADR-032):** a charter-capital step with no action is now a finding. ACB 2023Q1→Q2 (+15.0%) carries it (`pqt fundamentals ratios ACB --on 2023-08-01`); its action is recorded but cannot adjust, because ACB's bars start 2023-07-03 (accepted limit, backlog). In `e1d22bee4937dad9` v2, 129 rows carry the finding, all three real cases (MSN, VHM buyback, VJC placement); HPG 2024 was a missing action (row added) and HPG 2025Q3, TPB 2024Q4 a detector window fixed in `bedef68` (`evidence/sprint-8.md`, `sprint-8-1-steps.md`). Dividend yield is present only inside a recorded sweep: VNM 5.97% on 2024-12-02, MSN 0.00%, FPT absent. |
| 4 | Every absent ratio says why | Absence codes (`RatioAbsence`) listed for a name with no balance sheet, a trailing loss, a missing quarter, no dividend coverage | **Sprint 8**, over the 9,178 rows of `e1d22bee4937dad9` v2 (nine `_absent` columns): `dividend_history_incomplete` 6,699; `missing_quarter` 5,452; `no_close` 923; `equity_not_known_at_both_ends` 331; `no_charter_capital` 185; `trailing_loss` and `equity_not_positive` also occur. `dividend_history_incomplete` replaces sprint 7's 365-day proxy (`no_dividend_coverage`). Known exception, an absence not coded: GAS profit line 61 read as a known 0 on five shifted forms (backlog P1). |
| 5 | Surfaces show it | Console `FA`, `GET /instruments/{id}/ratios` and `/fundamentals` return the same numbers as the service for the same name and date; coverage row shows STB and GVR as not collected | `pqt fundamentals ratios FPT --on 2023-08-01` and `GET /instruments/{id}/ratios?on=2023-08-01` return the same numbers (EPS 4,573.37; P/E 18.4765; P/B 4.4435); `/fundamentals` returns 200; `pqt coverage STB|GVR` shows not collected. Console `FA` is covered by the frontend tests, not re-run by hand. |
| 6 | The dataset carries it (ADR-031) | Export of 3 names with `--fundamentals`; manifest v4 verifies in .NET and Python; v1–v3 manifests still verify; Python reader refuses the five cases in ADR-031 §7 | Dataset eee61890eba90fa9 (v4, 9,828 rows, 26 names × 378 sessions, 4 not collected): `dataset verify` passes in .NET and Python. v1–v4 fixture hashes agree in both languages. ADR-031 §7 refusals map to named tests (`evidence/sprint-7-c-dataset.md`). Window is 2023-07-03 → 2024-12-31 with `as_of 2024-08-05` (survivorship caveat stated there); a point-in-time export over 2023 is refused because VN30 coverage starts 2024-08-05. |
| 7 | No leak | As-of test over every column in `fundamentals.parquet`: a value at session D uses nothing published after D or after the dataset `known_as_of` | **Sprint 8 (2026-10-22, Tester + Audit):** `check_fundamentals_asof.py` over `e1d22bee4937dad9` v2: 9,178 rows checked, 0 failures. Negative control (every report's `known_from_utc` shifted +30 days): 8,601 failures. Sprint 7: 9,828 rows, 0 failures; control 9,851. |
| 8 | Suites green | `dotnet test` unit suite, Python tests, frontend tests, integration tests in Docker: counts and run date | **Sprint 8, 2026-10-22 at `962c15e` (Tester + Audit):** .NET unit 1,273/1,273; integration 206/206 (Docker, 4 min 23 s); Python 293 passed, 14 skipped (Qlib optional, ADR-017); frontend 28 files, 306/306. `Console.test.tsx` passed. A first frontend run alongside the integration suite failed to start 4 workers (timeout under load, the 4 files never ran); they passed on a re-run and the full run alone is 306/306. The flake stays in the UI/UX backlog. Sprint 7: unit 1,249, frontend 305/306. |
| 9 | Backlog listed, not hidden | The list below is in `handoff.md` with an owner or "later" for each item | `handoff.md` is git-ignored, so the backlog is the table below, with an owner or "later" per item (updated 2026-10-22 from the sprint 8 evidence; P1–P4 go to sprint 9, as the Moderator agreed on 2026-10-22). |

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
| P2: VPB 2025Q4 form recorded as 2024Q4; a `PeriodConflict` rule | sprint-8-3.md §4 | sprint 9 (proposed) |
| P3: ACB 2023 H1 separate form recorded as consolidated; rule 211 > 0 | sprint-8-3.md §3 | sprint 9 (proposed) |
| P4: separate reports with consolidated-only lines and no twin (MBB 2021Q4, POW, VCB, VIC 2022, ACB 2024 annual) | sprint-8-3.md §3 | sprint 9 (proposed) |
| ACB bars before 2023-07-03, so the 2023-06-01 actions do not adjust | sprint-8.md §8.1 | later |
| Dropped EPS lines that are not ×10^6 typos: VRE 2 (about 2× EPS), SSB 1 | sprint-8-3.md | later |
| MBB two capital values (repeated form); ACB 2022 Q1–Q3 forms with no values | sprint 7 backlog | later |
| Whether `SharesAt` counts stock-dividend shares correctly between the ex-date and the issue (2–5 weeks) | sprint-8-1-steps.md | later |
| Flaky `Console.test.tsx` | DoD #8 | UI/UX phase |
| GAS has no 2025Q1–Q3 forms, so every TTM after 2024Q4 has a gap and GAS `value_ep` and `quality_roe` are absent at every session of the 9.1 build | Tester + Audit, 2026-10-27, `tmp/audit-0e7d28336af399e1` | later (collection) |
| Circular 99 reports (the fiscal-2026 template) are held with no values: 99 in the database, 0 with values. Every name loses fundamentals from fiscal 2026 until they are read | Tester + Audit, 2026-10-27 | later (affects all names) |
| CafeF bars carry no turnover (0 of 22,545 daily bars since 2024), so `liquidity_20` is absent everywhere | Dev, 9.1 export, 2026-10-27 | decision pending (Moderator) |
| VN30 membership is sourced to 2026-09-17 while bars run to 2026-10-12; the next HOSE review table extends it | Dev, 9.1 export, 2026-10-27 | later (data) |

Closed in sprint 9: P1, GAS's five shifted forms, purged and re-imported with the shifted lines absent (`evidence/sprint-9-gas-p1.md`).

Closed in sprint 8: the ACB/SHB/SSB/VIB shared-form check (SHB, SSB, VIB are
consolidated; ACB is P3); the 22 refusals (confirmed correct); the 111 dropped
EPS lines (88 ×10^6 typos, 20 GAS money values, 3 above); HPG ×2 collected;
ACB and HPG capital steps (rows added); the detector's ex-date window
(HPG 2025Q3, TPB 2024Q4), fixed in sprint 8 (`bedef68`).

## Closing

**Sprint 8 (2026-10-22).** All nine criteria have evidence, re-run at the end of
sprint 8 (item 8.6). Lead closed sprint 8, and the operator declared **Phase 6
complete on 2026-10-22**.

At the end of Sprint 7 Lead fills the Evidence column from real runs (commands
and outputs kept under `docs/roadmap/evidence/`), the Moderator reviews, and the
operator declares Phase 6 complete or lists what is missing.
