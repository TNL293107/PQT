# Evidence: Sprint 8.2 (IDS capture gaps)

Run 2026-10-21 by Dev on the local Docker stack at `develop` `3a6b1d5`, with
the collector in `tools/ids-collector` (Edge, `--pause 3`, at most two
collectors at a time, no code edits while one ran). The filings stay in
`data/fundamentals/ids/`, which is git-ignored; nothing collected is committed.

## Counts before

| | Count |
| --- | --- |
| Filing documents on disk | 1,604 (HPG 54, VNM 49, STB 0, GVR 0) |
| IDS reports in `quant.financial_reports` | 1,582 |
| IDS reports with values | 745 |
| IDS statement lines | 237,004 |

These are already two more than ADR-029's 2026-10-20 re-import (1,580 /
743 / 236,398). The two are HPG's consolidated 2022Q2 and 2022 half-year
reports, fetched at 2026-10-21 13:17 UTC and imported before this run.

## HPG consolidated 2022Q2 and 2022 half year

```bash
.venv/Scripts/python -X utf8 collect.py --ticker HPG --from-year 2022 --to-year 2022 --pause 3 --out ../../data/fundamentals/ids
# 0 written, 12 already held, 0 failed
```

Searches with the dates widened (20/07–10/08, 11/08–15/09, 16/09–31/10/2022)
list all four filings for the two periods:

| Reference | Report | Sent |
| --- | --- | --- |
| `HPG-20220729-9521505d8534` | Hợp nhất, Quý 2.2022 | 2022-07-29 |
| `HPG-20220729-c739f94ddcf2` | Mẹ, Quý 2.2022 | 2022-07-29 |
| `HPG-20220829-6c78d1a5427f` | Hợp nhất, bán niên 30.06.2022 | 2022-08-29 |
| `HPG-20220826-54a686232870` | Mẹ, bán niên 30.06.2022 | 2022-08-26 |

**The gap is closed.** Both consolidated reports are held with values (326 and
280 lines).

**But each shares its form with the separate report (Tester).** Checked on the
documents: the `tables` of `9521505d8534` and `c739f94ddcf2` are identical, and
so are those of `6c78d1a5427f` and `54a686232870`. The separate reports were
imported with values on 2026-10-20, when their consolidated partners were not
yet held, so the `SharedForm` rule had nothing to compare them with. Their
values are the consolidated form. The next import refuses them as "already
held with values; purge it and import again".

Purge and re-import of HPG: **pending the Moderator's approval** (a delete
needs `fromModerator: true`). In scope: 54 reports, 8,341 statement lines and
54 raw payloads.

### After the purge

Lead ran the purge on 2026-10-22, with the Moderator's approval, with the deletes of
`backups/hpg-purge-2026-10-22/purge.sql` in one transaction (the backup was
written to the host first, not to the container's `/tmp`). It deleted 2 reports
(`HPG-20220729-c739f94ddcf2`, `HPG-20220826-54a686232870`), their 606
statement lines (326 + 280) and their 2 raw payloads. The rows are kept as
`bk_*.csv` beside the script. Dev then re-imported HPG:

```bash
pqt fundamentals import --provider IDS --ticker HPG
# 54 filings read; 0 recorded, 54 already held; 0 lines; refused 0;
# values withheld: 23 sharing the consolidated form
```

The two purged filings were recorded again at 03:34 UTC.

| Reference | Report | Values | Lines |
| --- | --- | --- | --- |
| `HPG-20220729-9521505d8534` | consolidated 2022Q2 | yes | 326 |
| `HPG-20220829-6c78d1a5427f` | consolidated 2022 H1 | yes | 280 |
| `HPG-20220729-c739f94ddcf2` | separate 2022Q2 | no (`SharedForm`) | 0 |
| `HPG-20220826-54a686232870` | separate 2022 H1 | no (`SharedForm`) | 0 |

| | Count |
| --- | --- |
| IDS reports | 1,582 |
| IDS reports with values | 743 (745 − 2) |
| IDS statement lines | 236,398 (237,004 − 606) |
| IDS raw payloads | 1,582 |
| Filings with no report (refused) | 22 (1,604 files − 1,582 reports), as in ADR-029 |

**Refused count not re-run.** The 22 is read from the counts, not from an
importer run. A full-provider import was not run: the harness denied it
in Dev's pane.

## VNM 2025 second quarter and half year

```bash
.venv/Scripts/python -X utf8 collect.py --ticker VNM --from-year 2025 --to-year 2025 --pause 3 --out ../../data/fundamentals/ids
# 0 written, 8 already held, 0 failed
```

Searches over 01/04–30/06, 01/07–31/08, 01/09–31/10 and 01/07–31/10/2025, and
01/11/2025–31/03/2026, list VNM's Q1, Q3, Q4 and annual 2025 reports (two
each). None is for Q2 or the half year: **01/07–31/10/2025 returns only the two
Q3 reports, and 01/07–31/08 returns nothing.** It is still missing, and is
named in ADR-029.

## STB and GVR

```bash
.venv/Scripts/python -X utf8 collect.py --ticker STB --from-year 2022 --pause 3 --out ../../data/fundamentals/ids
.venv/Scripts/python -X utf8 collect.py --ticker GVR --from-year 2022 --pause 3 --out ../../data/fundamentals/ids
```

The searches list the filings. Opening each one returns to the search page
instead of the filing, so the form never appears (`Timeout 30000ms` waiting
for *Chi tiết dữ liệu báo cáo*). This is the fault of 2026-10-15. All 2022
filings failed: STB 9 of 9, GVR 8 of 8. Over the same hours, HPG and VNM
searches and filings worked. The run was stopped after 2022, because further
years would only put load on a broken page. **Still not collected**; named in
ADR-029.

## Checks

FPT on 2023-08-01, before the purge: P/E 18.48, P/B 4.44, ROE 26.58%, shares
1,269,981,365. These match ADR-029 and sprint 7 C.

FPT on 2023-08-01, after the purge and on the backend rebuilt from `bedef68`:
P/E 18.48, P/B 4.44, ROE 26.58%, shares 1,269,981,365. These are unchanged.
The re-import counts are under **After the purge** above.
