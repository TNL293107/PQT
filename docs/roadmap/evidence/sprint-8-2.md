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

<!-- After the purge: counts after, the separate 2022Q2/H1 without values, the consolidated with values. -->

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

<!-- After the purge: FPT re-run, re-import counts. -->
