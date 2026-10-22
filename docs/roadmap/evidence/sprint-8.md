# Evidence: Sprint 8 (ratios version 2, ADR-032)

Run 2026-10-21/22 by Lead on the local Docker stack at `develop` `532ba10`
(8.1 and 8.4). More sections are added as 8.2, 8.3 and 8.6 close.

## 8.1 Capital step with no action

`pqt fundamentals ratios ACB --on 2023-08-01` now carries:

> Capital stepped +15.0% between 2023Q1 and 2023Q2 (₫33,774,351,000,000 →
> ₫38,840,504,000,000) with no corporate action that explains it; the share
> count is the balance sheet's.

Shares are unchanged (3,884,050,400). MSN on 2024-12-02 shows a second
case: +5.2% between 2024Q2 and 2024Q3 with no entitlement.

**Checked (QA, 2026-10-21, `sprint-8-1-steps.md`):** the ACB step is a missing
action (2022 dividend, 15% in shares, ex 2023-06-01, VSDC 159023), not a real
finding; its row is to be added. Of the 6 step pairs in the dataset, MSN, VHM
(buyback cancellation) and VJC (private placement) are real findings; HPG
2024Q2 is a missing action; HPG 2025Q3 and TPB 2024Q4 are false findings
caused by the detector's ex-date window (an amendment is proposed).

The ACB 2023 and HPG 2024 rows were added (VSDC 159023, 170903). HPG's
computes. ACB's two 2023-06-01 actions do not become adjustments, because ACB's
stored bars start 2023-07-03 ("No daily bar is stored before the ex-date"),
so `pqt fundamentals ratios ACB --on 2023-08-01` still carries the +15.0%
finding. This is the stored-bar window limit (ADR-033), not a detector fault;
no row of a dataset starting on or after 2024-08-05 is affected. Accepted by
Lead on 2026-10-21, with no backfill of one name.

## 8.4 Corporate actions of the eight names and dividend coverage

24 rows were added (data/fixtures/corporate-actions.csv, 75 → 99), read from
VSDC notices, with `data/fixtures/sources/entitlements-eight-names.md`.
Eight coverage records: `complete_from` 2023-07-01 (VPL 2025-05-13),
`swept_on` 2026-10-21.

| name | session | dividend yield |
| --- | --- | --- |
| VNM | 2024-12-02 | 5.97% = (500 + 900 + 2,450) ÷ 64,500 |
| VNM | 2025-12-01 | 8.24% = (500 + 2,000 + 2,850) ÷ 64,900 |
| MSN | 2024-12-02, 2025-12-01 | 0.00%, a known zero inside the sweep |
| FPT | 2024-12-02, 2025-12-01 | absent: no sweep of this issuer's corporate actions is recorded |
| ACB | 2024-12-02, 2025-12-01 | absent, same reason |

Test `VNM_swept_from_July_2023_has_a_yield_from_July_2024_until_the_sweep_date`:
absent at 2024-06-28, 3,850 ÷ 70,000 at 2024-07-01 and 2024-09-30, absent at
2026-10-22.

## Dataset at ratio version 2

Dataset `e1d22bee4937dad9` (VN30 as of 2024-08-05, 2024-08-05 → 2025-12-31,
`--fundamentals`): schema 4, ratio version 2, 9,178 rows, 4 not collected.

- `pqt dataset verify`: 2 files match the manifest. Python opens it
  (ratio_version 2) through `check_fundamentals_asof.py`.
- As-of check over every row: **0 failures**.
- The five most frequent absence codes over the nine `_absent` columns
  (`trailing_loss` and `equity_not_positive` also occur, less often):

| code | cells |
| --- | --- |
| `dividend_history_incomplete` | 6,699 |
| `missing_quarter` | 5,452 |
| `no_close` | 923 |
| `equity_not_known_at_both_ends` | 331 |
| `no_charter_capital` | 185 |

- Dividend yield is present on 2,237 of 9,178 rows.
- Rows carrying a "Capital stepped" finding: 356 across 6 name–step pairs.
- Reading an export with a `known_as_of` before the sweeps were imported gives
  `dividend_history_incomplete`, because a sweep's recording time is its import
  time (ADR-032).

Tests at this commit: .NET unit 1,267/1,267; Python manifest and fundamentals
tests pass; both reproduced by Lead on 2026-10-21.
