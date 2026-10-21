# Evidence: fundamentals in the dataset (Sprint 7 C, ADR-031)

Run 2026-10-21 by Dev (claude-1) on the local Docker stack, at `develop`
`ac61537` (C is `549e8fe`). This file covers Phase 6 definition of done,
criteria **3** (share-count checks for ACB), **6** (the dataset carries it) and
**7** (no leak). The other criteria are not covered here.

## The export

VN30 membership is recorded only from 2024-08-05 (`coverage_from`). A
point-in-time export over 2023 is therefore refused:

```
pqt dataset export --universe VN30 --from 2023-01-03 --to 2023-12-29 --fundamentals
Membership of VN30 is not known on every session from 2023-01-03 to 2023-12-29 (OutsideCoverage). ...
```

The export below reads the constituent set of 2024-08-05 instead and applies it
to the whole window (`membership: as_of`). That carries survivorship bias, which
the manifest states. It does not affect what this file checks.

Stored daily bars start on **2023-07-03** for every name. Sessions are the dates
some exported bar opened on, so fundamentals rows run from 2023-07-03 to
2024-12-31: 378 sessions. ACB's 2023 Q2 capital step falls inside that range.

```
MSYS_NO_PATHCONV=1 docker compose exec -T backend dotnet cli/PersonalQuant.Cli.dll \
  dataset export --universe VN30 --as-of 2024-08-05 --from 2023-01-03 --to 2024-12-31 --fundamentals

dataset                 eee61890eba90fa9 v1
schema version          4
content hash            55215fa6b999ccf66345fd78f041af7babdaf19d2149994d30c5f6cc890a4d1e
universe                VN30 as of 2024-08-05
window                  2023-01-03 → 2024-12-31 1d
adjustment              adjusted, strict announcements
known as of             current series
history                 none: rows only where each instrument was a member
fundamentals            ratio version 1, IDS, cut session_date_00utc, 9828 rows, 4 not collected
instruments             30
rows                    21168

bars.parquet  347,727 bytes  sha256:04b8b97c3fce45138a4d26f5b442cad779779734a25fb95875484dfb9f55b53d
fundamentals.parquet  586,277 bytes  sha256:8955f1e905c9fd5fc999c5e36925cf440574a3396feb33824921c7a4b57de4aa

SSI: no fundamentals (out_of_scope); no rows.
GVR: no fundamentals (ids_capture_gap); no rows.
STB: no fundamentals (ids_capture_gap); no rows.
BVH: no fundamentals (out_of_scope); no rows.
```

The 9,828 rows are 26 names × 378 sessions. All 30 constituents have bars
over the window; four are not collected.

The dataset is copied out with `docker compose cp backend:/app/datasets/eee61890eba90fa9/v1 …`
and is not committed (IDS and CAFEF grant no licence to redistribute).

## Criterion 6: the dataset carries it

**.NET verifies the build:**

```
pqt dataset verify eee61890eba90fa9
eee61890eba90fa9 v1: 2 files match the manifest.
```

**Python verifies the build.** `LocalDatasetProvider.verify` checks the file
hashes, the content hash and every reader refusal. The leak check below opens
the build through it, and its first line is:

```
dataset eee61890eba90fa9 v1: 9828 rows checked
```

**v1 to v3 manifests still verify, and the two languages agree on v4.** Each
committed fixture hashes to its recorded `content_hash` in both languages:
- .NET: `DatasetManifestGoldenTests`, 22/22 manifest tests.
- Python:

```
test_the_content_hash_matches_the_one_dotnet_recorded[v1-as-of.json] PASSED
test_the_content_hash_matches_the_one_dotnet_recorded[v2-point-in-time.json] PASSED
test_the_content_hash_matches_the_one_dotnet_recorded[v2-known-as-of.json] PASSED
test_the_content_hash_matches_the_one_dotnet_recorded[v3-history.json] PASSED
test_the_content_hash_matches_the_one_dotnet_recorded[v4-fundamentals.json] PASSED
```

**The reader refuses the five cases of ADR-031 §7**, in
`quant/tests/test_fundamentals.py`:

| ADR-031 §7 case | Test |
| --- | --- |
| row on a session the instrument was not a member | `test_a_row_on_a_session_the_instrument_was_not_a_member_is_refused` |
| row for an instrument in `not_collected` | `test_a_row_for_an_instrument_not_collected_is_refused` |
| unknown `ratio_version` | `test_an_unknown_ratio_version_is_refused` (also in `test_dataset_manifest.py`) |
| value present with its `_absent` reason | `test_a_value_present_beside_its_absence_reason_is_refused` |
| listed `fundamentals.parquet` missing from disk | `test_a_listed_fundamentals_file_missing_from_disk_is_refused` |

The reader also refuses five cases the ADR does not list:
- a value absent with no reason;
- a `fundamentals` object without the file;
- the file without the object;
- a row count that disagrees with the manifest;
- a `not_collected` id that is not exported, or malformed JSON arrays.

Python runs `test_dataset_manifest.py`, `test_fundamentals.py` and
`test_datasets.py`: 85 passed.

## Criterion 7: no leak

`check_fundamentals_asof.py` (in this directory) holds every row of the real
export against the reports and corporate actions in the database. A row's cut is
its `known_as_of_utc`. For every row, the script checks five things:
1. Every report in `report_ids` was known by the cut.
2. The balance-sheet quarter has been published by the cut: no report with
   values, of the row's scope and known by then, has a later period.
3. The last TTM quarter likewise.
4. `actions_applied` is no more than the share-count actions that went ex and
   were announced by the session.
5. The cut is the session's 00:00 UTC.

```
docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "\copy (select id, instrument_id, scope, kind, fiscal_year, fiscal_quarter, known_from_utc, values_filed from quant.financial_reports) to stdout with csv header"' > reports.csv
docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "\copy (select a.instrument_id, a.ex_date, a.share_factor, a.announced_on, c.is_cancelled from quant.price_adjustments a join quant.corporate_actions c on c.id=a.corporate_action_id) to stdout with csv header"' > adjustments.csv
cd quant && .venv/Scripts/python -I ../docs/roadmap/evidence/check_fundamentals_asof.py <export-root> eee61890eba90fa9 reports.csv adjustments.csv

dataset eee61890eba90fa9 v1: 9828 rows checked
0 checks failed: a value uses something published after its cut
```

**Negative control:** the same check with every report's `known_from_utc`
moved 30 days later fails, so the check can see a leak.

```
LEAK 01a1341d-47cb-7022-85fd-dfd9bbe73da3 2023-08-11: report 01a1575c-6f3d-7452-af79-584bc06e26fa known 2023-08-18 17:00:00+00:00, after the cut
LEAK 01a1341d-47cb-7022-85fd-dfd9bbe73da3 2023-08-11: balance_sheet_quarter 2023Q2 is later than any report known by the cut ((2023, 1))
9851 checks failed: a value uses something published after its cut
```

**A real before/after pair: FPT's 2023 Q2 report.** It became known at
2023-07-19 17:00 UTC. The last session before it still reads 2023 Q1, and the
first session after it reads Q2:

| session | balance sheet | TTM to | shares | P/E | report_ids |
| --- | --- | --- | --- | --- | --- |
| 2023-07-19 | 2023Q1 | 2023Q1 | 1,261,580,558 | 17.84 | `01a1575c-1038-…` |
| 2023-07-20 | 2023Q2 | 2023Q2 | 1,269,981,365 | 17.56 | `01a1575c-6f3d-…` |

## Criterion 3, in part: FPT, VCB and ACB in the export

Values are rounded for display. The file holds them at full precision.

| name | session | BS | TTM to | close | shares | EPS | P/E | P/B | ROE | yield | absent | actions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| FPT | 2023-07-03 | 2023Q1 | 2023Q1 | 86,400 | 1,097,026,572 | 5,058.86 | 17.08 | 4.20 | 26.57% | 0.00% | | 0 |
| FPT | 2023-08-01 | 2023Q2 | 2023Q2 | 84,500 | 1,269,981,365 | 4,573.37 | 18.48 | 4.44 | 26.58% | 1.18% | | 1 |
| FPT | 2024-12-02 | 2024Q3 | 2024Q3 | 142,200 | 1,460,448,066 | 5,127.95 | 27.73 | 6.99 | 28.09% | 0.70% | | 0 |
| VCB | 2023-08-01 | 2023Q2 | 2023Q2 | 91,200 | 5,589,102,105 | 5,804.73 | 15.71 | 3.36 | 23.61% | — | yield: `no_dividend_coverage` | 1 |
| VCB | 2024-12-02 | 2024Q3 | 2024Q3 | 94,200 | 5,589,091,300 | 6,202.34 | 15.19 | 2.77 | 19.84% | 0.00% | | 0 |
| ACB | 2023-08-01 | 2023Q2 | — | 22,850 | 3,884,050,400 | — | — | 1.41 | — | — | EPS, P/E, ROE: `missing_quarter`; yield: `no_dividend_coverage` | 0 |
| ACB | 2023-12-01 | 2023Q3 | 2023Q3 | 21,950 | 3,884,050,400 | 3,838.51 | 5.72 | 1.28 | — | — | ROE: `equity_not_known_at_both_ends`; yield: `no_dividend_coverage` | 0 |

FPT on 2023-08-01 matches the figures checked by hand in sprint 7 B: P/E 18.48,
P/B 4.44, ROE 26.58%.

**ACB, 2023 Q2 capital step: the export carries no finding, as ADR-030 says it
should not.** Capital went from the 2023Q1 balance sheet to the 2023Q2 one with
no corporate action recorded near it. The shares column follows the balance
sheets:

| balance sheet | sessions | shares |
| --- | --- | --- |
| 2023Q1 | 2023-07-03 → 2023-07-28 | 3,377,435,100 |
| 2023Q2 | 2023-07-31 → 2023-10-24 | 3,884,050,400 (+15.0%) |
| 2023Q3 | 2023-10-25 → 2024-04-25 | 3,884,050,400 |
| 2024Q1 | 2024-04-26 → 2024-07-23 | 3,884,050,400, then 4,466,657,960 after the 2024 bonus is applied (`actions_applied` 1) |
| 2024Q2 | 2024-07-24 → 2024-10-24 | 4,466,657,900 |

Two of these rows are worth reading:
- **2023 Q2 step.** ADR-030 "The two checks, as implemented" says a step with
  no action near it, such as ACB's 15% rise in 2023 Q2, is not checked, because
  nothing is applied on it. So no row of ACB carries a finding (0 of 378). The
  step is visible in `shares` and stays in the backlog as "ACB +15% capital in
  2023 Q2 without an action".
- **2024 bonus.** The bonus is applied on top of the 2024Q1 count, then appears
  in the 2024Q2 capital. The count matches to 60 shares, within rounding.

**Absence codes across the export**, counted over the nine `_absent` columns:

| code | cells |
| --- | --- |
| `no_dividend_coverage` | 8,788 |
| `missing_quarter` | 3,724 |
| `trailing_loss` | 212 |
| `equity_not_known_at_both_ends` | 209 |
| `no_charter_capital` | 185 |
