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

## Dataset re-export after the window fix (`bedef68`)

Run 2026-10-22 by Dev, with the backend rebuilt from `bedef68`. This was after the HPG
purge and re-import (sprint-8-2.md) and with the ACB 2023 and HPG 2024 rows of
8.1 in the database. Same command and window as v1:

```bash
pqt dataset export --universe VN30 --as-of 2024-08-05 --from 2024-08-05 --to 2025-12-31 --fundamentals
# dataset e1d22bee4937dad9 v2, schema 4, ratio version 2, 9,178 fundamentals rows, 4 not collected
# content hash 503030f7496f89cba0afc8c9272c3bca4a0a26ce4957d7a89f3e8ebca69bd1dc
```

The id is unchanged, because it names the request; the content is version **v2**.

- `pqt dataset verify e1d22bee4937dad9 --version 2`: 2 files match the manifest.
- `check_fundamentals_asof.py` over v2: 9,178 rows checked, **0 failures**.
- The absence codes and dividend yield (present on 2,237 rows) are the same as v1.
- Rows carrying "Capital stepped" go from **356 to 129**:

| name | step | rows v1 | rows v2 | why |
| --- | --- | --- | --- | --- |
| HPG | 2024Q1 → 2024Q2, +10.0% | 122 | 0 | the HPG 2024 bonus row (VSDC 170903, ex 2024-05-23) is now recorded: a missing action, not the window |
| HPG | 2025Q2 → 2025Q3, +20.0% | 45 | 0 | window fix: ex 2025-06-26, 4 days before the prior quarter end, factor 1.20 matches |
| TPB | 2024Q3 → 2024Q4, +20.0% | 60 | 0 | window fix: ex 2024-09-23, factor 1.20 matches |
| MSN | 2024Q2 → 2024Q3, +5.2% | 65 | 65 | real finding, kept |
| VHM | 2024Q1 → 2024Q4, −5.7% | 23 | 23 | real finding (buyback cancellation), kept |
| VJC | 2025Q1 → 2025Q2, +9.2% | 41 | 41 | real finding (placement), kept |

The expected ~251 counted only the window fix (356 − 105). The extra 122 come
from the HPG 2024 row of 8.1. Every remaining finding is one of the three real
ones from `sprint-8-1-steps.md`.

## 8.6 Close: the Phase 6 definition of done re-run

Run 2026-10-22 by Tester + Audit at `develop` `962c15e`, read-only (outputs in
`tmp/audit-8-6/out/`, git-ignored); reviewed by QA + QC. The result is entered
in `docs/roadmap/phase-6-definition-of-done.md` (`4cba51e`).

- **Audit mirror and database (DoD #1):** `filings 1604 refused 22 withheld 692
  shared 688 dropped 111`. The database holds 1,582 reports, 743 with values,
  236,398 statement lines; the diff between mirror and database is empty. The
  refused set is the 22 of 8.3. HPG's separate 2022Q2 and H1 are held without
  values (`SharedForm`), and the consolidated pair with 326 and 280 lines.
- **As-of (DoD #7):** `e1d22bee4937dad9` v2, 9,178 rows checked, 0 failures.
  Negative control (`known_from_utc` shifted +30 days): 8,601 failures.
- **Suites (DoD #8):** .NET unit 1,273/1,273; integration 206/206 (Docker,
  4 min 23 s); Python 293 passed, 14 skipped (Qlib optional, ADR-017);
  frontend 28 files, 306/306.
- **Frontend run note:** a first frontend run, made while the integration suite
  was running, exited 1 because 4 forks workers timed out on start
  (`App.test.tsx`, `AppShell.test.tsx`, `Console.test.tsx`,
  `InstrumentSearchDialog.test.tsx` never ran; 261 other tests passed). The 4
  files then passed alone (45/45), and the full run alone gave 306/306. This is
  a worker start-up timeout under load, not a test failure.

Sprint 8 closed by Lead on 2026-10-22. Declaring Phase 6 and pushing are the
Moderator's call.
