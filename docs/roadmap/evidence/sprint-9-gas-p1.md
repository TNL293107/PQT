# Evidence: GAS P1 (shifted income lines), purge and re-import

Run 2026-10-27 by Dev on the live Docker stack, from a clean worktree of
`main` `bf3e14e`, which contains the fix (`7301f5c`) and its guard tests
(`49dd4b7`). Approved by the Moderator in the room (seq 204, "cho phép purge +
import GAS Option B") and at the permission prompt in Dev's pane. The finding is
sprint 8.3 §5, item P1.

The CLI ran on the host against the live database. Its Postgres settings were
read from the live backend container into the process environment only; nothing
was printed or written. No container was restarted or recreated.

## The five forms

`GAS-20220429-eafecbfc6049`, `GAS-20221028-fe110add5b3f`,
`GAS-20230130-cb4708826d28`, `GAS-20230428-6245c9318c1e` and
`GAS-20240426-ba25b2a787c4`: consolidated Circular 200 quarterlies whose income
lines from 61 down sit one row low.

## Before

| | Count |
| --- | --- |
| Reports | 5 |
| Statement lines | 1,609 |
| Raw payloads | 5 |

`tmp/audit-gas-scan/shift_stored.sql` (read-only, ends in `ROLLBACK`) found
**20 columns on 5 forms**: 4 per form (quarter and year to date, this year and
last), all consolidated, with 61 = 0, 62 between 96.77% and 98.74% of 60, 70 not
stored (it held an amount of money and the EPS rule dropped it) and 71 present.

GAS 2024Q1 in `GAS-20240426-ba25b2a787c4`, quarter and year to date alike:
60 = 2,543,605,417,303, **61 = 0**, 62 = 2,511,656,345,896, 71 = 1,093.

`pqt fundamentals ratios GAS --on 2024-11-01`: profit quarters 2023Q4–2024Q3,
net profit (TTM) ₫8,565,628,547,164, **P/E 19.20**, ROE 14.31%: a zero quarter
counted as known.

## Backup and source files

`backups/gas-purge-2026-10-27/` in the `PQT-gas` worktree (git-ignored):
`bk_financial_reports.csv` (5 rows), `bk_statement_lines.csv` (1,609) and
`bk_fundamentals_raw_payloads.csv` (5), written to the host before the purge.
`ids/` holds the five collector documents, exported byte for byte from the
stored payloads; each file's SHA-256 equals the stored checksum, checked twice
(before the backup and again just before the purge).

## Purge

`purge.sql`, one transaction, deleting lines and reports only; the payloads are
kept, since the re-import reads the files exported from them:

```
 before |       5 |  1609 |        5
DELETE 1609
DELETE 5
 after  |       0 |     0 |        5
COMMIT
```

## Import

```bash
MarketData__IdsFilingsDirectory=…/gas-purge-2026-10-27/ids \
  pqt fundamentals import --provider IDS --ticker GAS
```

| | |
| --- | --- |
| filings read | 5 |
| reports recorded | 5 |
| without values / already held / late / refused / withheld | 0 |
| lines recorded | 1,549 |
| lines restated | 0 |
| lines dropped | 20 |

The 20 dropped entries are the 20 columns the scan found, each reported as
"lines 61 to 71 … sit one row low (61 is 0, 70 is 60 less 62); 61, 62, 70 and 71
are dropped, so profit to the parent is absent, not zero". 1,609 − 1,549 = 60
lines: 61, 62 and 71 of each of the 20 columns (70 was never stored).

The payloads were not added again: the five references still hold 5, as the
import checks each checksum before it stores one.

## After

| Check | Result |
| --- | --- |
| Reports / lines / payloads | 5 / 1,549 / 5 |
| `shift_stored.sql` | 0 rows (both queries) |
| GAS 2024Q1 lines 61, 62, 70, 71 | none; only 60 remains, quarter and year to date |
| P/E at 2024-11-01 | absent: "profit for 2024Q1 is not known; four consecutive quarters are needed" (`MissingQuarter`), not 19.20 |

Net profit (TTM), EPS and ROE at 2024-11-01 are absent for the same reason.
Book value per share (24,864.07) and P/B (2.82) are unchanged, since they read
the balance sheet only.

The fix makes the five quarters' profit **unknown**, not corrected. Sprint 8.3 §5
estimated P/E at about 14.8 with the shifted line read back up; that estimate is
not what the data now says, because shifting the lines back would be a guess
recorded as a fact (ADR-029).

The dropped columns cover 2021Q1, 2021Q3, 2021Q4, 2022Q1, 2022Q3, 2022Q4, 2023Q1
and 2024Q1. Three of them are stated again, correctly, in the prior-year column
of a later form: 2021Q4 by `GAS-20220121-60668c51982d`, 2022Q3 by
`GAS-20231030-7608662c06c9` and 2022Q4 by `GAS-20240130-2e944b619f77`. From the
day each of those is known, that quarter has a profit again. The other five
quarters have none, so a GAS session whose trailing year includes one of them
has no TTM profit, EPS, P/E or ROE. Checked at 2023-06-01: absent, "profit for
2021Q3 is not known", since `GAS-20231030` was not yet known then.

## Point in time

The reports were recorded again at 2026-10-27 14:53 UTC. As with the HPG purge
of sprint 8.2, a `knownAsOf` read before that instant sees no GAS values for
these five forms: what the system believed then was the shifted reading, and
that reading is gone rather than superseded.

## Not done here

- No dataset was re-exported. The 9.1 export (`tmp/requests/vn30-sprint-9-1.json`)
  runs next and reads the corrected values.
