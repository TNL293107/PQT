# Phase 6 — definition of done (Sprint 7, item 7.4)

Status: DRAFT by Lead. Each criterion names the evidence that closes it.
Evidence is filled in at the end of Sprint 7; a criterion with no evidence is
not met. The operator declares Phase 6 complete; this document does not.

## Criteria

| # | Criterion | Evidence required | Evidence |
| --- | --- | --- | --- |
| 1 | Filings are held for the universe, with gaps named | Count of held filings per ticker from `data/fundamentals/ids`; STB and GVR listed as not collected, HPG ×2 and VNM 2025 Q2/H1 as missing periods | _fill at close_ |
| 2 | Statements are read as of a date | Reads for 3 real names at 2 dates each; a report filed after the date does not change the value (synthetic restatement test plus one real before/after pair) | _fill_ |
| 3 | Ratios follow ADR-030 | Hand-computed P/E, P/B, ROE, EPS, dividend yield for one real name; share-count checks and findings shown for ACB (2023 Q2 capital step) | _fill_ |
| 4 | Every absent ratio says why | Absence codes (`RatioAbsence`) listed for a name with no balance sheet, a trailing loss, a missing quarter, no dividend coverage | _fill_ |
| 5 | Surfaces show it | Console `FA`, `GET /instruments/{id}/ratios` and `/fundamentals` return the same numbers as the service for the same name and date; coverage row shows STB and GVR as not collected | _fill_ |
| 6 | The dataset carries it (ADR-031) | Export of 3 names with `--fundamentals`; manifest v4 verifies in .NET and Python; v1–v3 manifests still verify; Python reader refuses the five cases in ADR-031 §7 | _fill_ |
| 7 | No leak | As-of test over every column in `fundamentals.parquet`: a value at session D uses nothing published after D or after the dataset `known_as_of` | _fill_ |
| 8 | Suites green | `dotnet test` unit suite, Python tests, frontend tests, integration tests in Docker: counts and run date | _fill_ |
| 9 | Backlog listed, not hidden | The list below is in `handoff.md` with an owner or "later" for each item | _fill_ |

## Known limits that do not block Phase 6

- Scope: banks and non-financials only in the first import; SSI, TCX, BVH are out of scope (`out_of_scope`).
- Eight tickers (BCM, MSN, MWG, PLX, POW, VNM, VPL, VRE) have no corporate actions
  recorded: share count is not adjusted and dividend yield is absent.
- Dividend-coverage test is a proxy (earliest action newer than 365 days).
- VPL has filings only from 2025.

## Backlog (from sprint-7-plan-draft.md)

VNM 2025 Q2/H1 missing; ACB/SHB/SSB/VIB shared-form scope needs a PDF check; 111
EPS lines above ₫1,000,000 dropped; ACB +15% capital in 2023 Q2 without an
action; MBB two capital values (repeated form); ACB 2022 Q1–Q3 forms carry no
values; STB, GVR, HPG ×2 capture gap; 22 filings refused by the importer.

## Closing

At the end of Sprint 7 Lead fills the Evidence column from real runs (commands
and outputs kept under `docs/roadmap/evidence/`), the Moderator reviews, and the
operator declares Phase 6 complete or lists what is missing.
