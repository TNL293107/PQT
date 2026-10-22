# Evidence: Sprint 8.3 (scope and refusals)

Run 2026-10-21 by Tester + Audit at `develop` `3a6b1d5`, read-only: no import was
run, and no code or data was changed. The inputs are the 1,604 collector files under
`data/fundamentals/ids/` and the local database (`quant.financial_reports`,
`quant.statement_lines`). The scripts and SQL are in `tmp/audit-8-3/`, which git
ignores. Commands are under **Reproduce**.

## Summary

| item | result |
| --- | --- |
| Refused filings | **22**, as ADR-029 says: 11 `Unclassified` (all LPB) and 11 `ImplausibleDate`. The 28 in Lead's notes on the B0 re-import is unconfirmed: no commit, document or data state gives it. The 22 replaces it. |
| Refusals correct? | All 22 are correct under the rules. Three of them leave consolidated values recorded under a separate report (VCB, VIC, ACB). |
| New refusals pending | **2**: HPG's separate 2022Q2 and H1. The next import refuses them as `SharedForm` (finding 1). *Outcome: purged and re-imported 2026-10-22, recorded without values, not refused; refused stays 22 (sprint-8-2.md).* |
| Dropped EPS lines | **111**, matching ADR-029 by name. **88** are ×10^6 typos. **23 are not**: GAS 20, VRE 2 and SSB 1 (finding 3). |
| Shared form, ACB/SHB/SSB/VIB | One consolidated PDF of each was checked against the form (section 3). **SHB, SSB and VIB: consolidated**, with total assets equal to the million. **ACB 2023 H1: the form is not the consolidated statement.** It is the separate one, recorded as consolidated. |

Findings that need a code or data change go to Lead as a probe. Nothing was edited.

## 1. HPG 2022Q2 and H1: separate reports hold the consolidated form

Dev's 8.2 collection added HPG's consolidated 2022Q2 report (`HPG-20220729-9521505d8534`)
and its consolidated 2022 half-year report (`HPG-20220829-6c78d1a5427f`). Both were
recorded 2026-10-21 13:30 UTC with values. Each form is equal, cell for cell, to the
form of the separate report of the same period: `HPG-20220729-c739f94ddcf2` and
`HPG-20220826-54a686232870`. Those separate reports have held values since the B0
re-import of 2026-10-20.

- The separate reports' forms carry lines that only a consolidated statement fills:
  429 is ₫123.8bn, 61 is ₫4,032bn for Q2 and ₫12,249bn for H1, and 62 is filled. So
  the database's separate HPG 2022Q2 and H1 values are the consolidated ones.
- The next import refuses both separate reports as `SharedForm` ("already held with
  values; purge it and import again"). Refused filings then come to 24.
- Fix: purge the two separate reports and import again. Per Lead, this goes to Dev
  after the 8.2 collection. It is not run here.
- **Outcome (2026-10-22):** after the purge, the re-import recorded both separate
  reports without values (`SharedForm`, withheld), not refused, so refused filings
  stay at 22. See `sprint-8-2.md`, "After the purge".

## 2. Refused filings (22)

The database holds 1,582 reports. The collector files hold 1,604. The 22 filings with
no report are exactly the 22 that the mirror of the importer's rules refuses. For each
of the 1,582 reports, the mirror's values-filed flag and line count agree with the
database (236,398 lines), except the two HPG reports of finding 1.

"Same form as" means the form is equal, cell for cell, to that filing's form.
"Indexed" is IDS's indexed date.

| filing | reason | what it is | correct? | consequence |
| --- | --- | --- | --- | --- |
| LPB ×11, 2021Q4 to 2024Q2 quarterlies | Unclassified | Named "Báo cáo tài chính quý N/ năm Y". Neither the name nor the abstract states a scope, and the document names end in "CA". The forms have no minority line. | Yes: the scope is stated nowhere | LPB has no quarterly values from 2021Q4 to 2024Q2. Its H1 and annual reports are separate. Kept as a known gap (see Known gaps). |
| HDB-20220125-697506258120, -85c782a90a03 | ImplausibleDate | The 2021Q4 separate and consolidated reports, but the form says 2022Q4. Same form as HDB-20230131-* (2022Q4). IDS shows the next year's form. | Yes | HDB 2021Q4 quarterly has no values. The 2021 annual reports are held. |
| ACB-20240307-4d1a9a642781 | ImplausibleDate | Consolidated 2023 annual. The form says 2024. Same form as ACB-20250305-aeb6b673d868 (2024 annual, separate). | Yes | ACB's 2024 separate annual holds the consolidated form: its total assets of ₫864,005.7bn equal the consolidated 2024Q4 quarterly's. |
| BCM-20231226-1daba1c25e1b | ImplausibleDate | Combined (separate) 2023Q4, sent 2023-12-26 but indexed 2024-01-30. Document dated 20240130. Same form as the consolidated BCM-20240129-cbf5a81deaff. | Yes | None: the form is recorded under the consolidated report. |
| MWG-20220127-d933d1393b68 | ImplausibleDate | Parent's 2021 annual, sent 27 days after year end, indexed 2022-03-21. Same form as the consolidated annual MWG-20220320-fb91d57c8f9c. | Yes | None. |
| VCB-20230130-28792fb5df3b | ImplausibleDate | The known mislabelled "annual 2022" from 30 days after year end, indexed 2023-03-31. Same form as VCB-20230331-f0713d5716e3 (separate annual). | Yes | VCB's 2022 separate annual holds the consolidated form (minority 700 = ₫88.4bn), with no consolidated annual beside it. |
| VHM-20230321-38168866c79b | ImplausibleDate | Parent's 2023 half year, sent 2023-03-21 but indexed 2023-08-29. Same form as the consolidated H1. | Yes | None. |
| VHM-20240129-15c687c82771 | ImplausibleDate | Parent's 2023Q4 report. The form says 2024Q4: same form as VHM-20250124-*. | Yes | None: the consolidated 2023Q4 report is held. |
| VIC-20220830-c95f4d18097d | ImplausibleDate | Consolidated 2022 annual, sent 2022-08-30 but indexed 2023-04-02. Same form as VIC-20230401-64446f39a2a2 (separate annual). | Yes | VIC's 2022 separate annual holds the consolidated form (429 = ₫25,563bn), with no consolidated annual beside it. |
| VPB-20250122-3170f370ce81, -f5a535b220f8 | ImplausibleDate | The abstract says 2024Q4. The form says 2025Q4 but holds 2024's figures: closing total assets ₫923,848bn. | Yes | VPB's 2024Q4 quarterly is missing. See finding 4. |

Four sent dates (BCM, MWG, VHM 2023, VIC) are wrong while the indexed dates are
right. The importer records the later of the two dates but checks the sent date, so
these filings are refused rather than dated by the indexed date. Changing that would
recover nothing: all four forms are already recorded under a sibling report.

## 3. Shared form: ACB, SHB, SSB, VIB

### PDF spot check

After the 8.2 collection ended, Lead approved fetching four consolidated half-year
PDFs from IDS by hand: one browser, a 3-second pause, files only under
`tmp/audit-8-3/pdf/`. Each PDF's consolidated balance sheet (form B02a/TCTD-HN) was
compared with the form recorded under that filing. Figures are in ₫ millions, as
printed.

| filing (recorded as consolidated) | PDF title | PDF total assets | form total assets (300) | other lines | verdict |
| --- | --- | --- | --- | --- | --- |
| ACB-20230814-7a97bb04ed90 (2023 H1) | Báo cáo tài chính hợp nhất giữa niên độ, 30/6/2023 (KPMG) | 630,893,170 | 627,365,458 | The PDF has no investment in subsidiaries (IX holds only lines 4 and 5); the form has 211 = 3,505,000. | **The form is not the consolidated statement.** The PDF matches the consolidated 2023Q2 quarterly (630,893,169). |
| SHB-20240829-a453ffb035ce (2024 H1) | Báo cáo tài chính hợp nhất giữa niên độ đã được soát xét, 30/6/2024 (Deloitte) | 659,861,729 | 659,861,729 | Investment in associates 350,938; no subsidiaries line. | **Consolidated.** |
| SSB-20240815-3b369738ee87 (2024 H1) | Báo cáo tình hình tài chính hợp nhất, 30/6/2024 | 280,657,951 | 280,657,951 | Goodwill 302,623 is shown, so there is a subsidiary. | **Consolidated.** |
| VIB-20240806-e52da30142f5 (2024 H1) | Báo cáo tình hình tài chính hợp nhất, 30/6/2024 | 430,962,080 | 430,962,080 | Charter capital 25,368,075 equals the form's 411. | **Consolidated.** |

One period each for SHB, SSB and VIB, as the brief asked. Their other periods rest on
the two tests below, which fit consolidated on every pair. ACB's separate 2023 H1 PDF
was not fetched, so the ₫627,365bn is shown to be not consolidated, not proven to be
the separate figure. Line 211 > 0 makes the separate reading near certain.

### Tests on the forms

Two tests were run on the forms themselves, over every period:

- **Bank line 211, investment in subsidiaries.** Consolidation eliminates it, so it
  is 0 on a consolidated statement and carries the cost of the subsidiaries on a
  separate one.
- **Total assets (300) on the same date.** A quarterly report and a half-year or
  annual report closing on the same day should agree, up to review adjustments.

Results:

| name | 211 on the forms | quarterly vs H1/annual, same date | reading |
| --- | --- | --- | --- |
| SHB | 0 on all 57 | within 0.08% on 11 pairs | Fits consolidated. Not proof: SHB's subsidiaries are small. |
| SSB | 0 on all 55 | within 0.02% on 10 pairs | Fits consolidated. Not proof. |
| VIB | 0 on all 55 | within 0.08% on 10 pairs | Fits consolidated. Not proof. |
| ACB | 0 on 40 forms; **₫3,505bn on the 2023 half-year form** (both filings) | 2023 H1 is **−0.56%** against the 2023Q2 quarterly; the other 5 pairs agree exactly | The 2023 H1 form is **the separate one**. |

**ACB 2023 half year (`ACB-20230814-7a97bb04ed90`, recorded as consolidated).**
Its form states investment in subsidiaries of ₫3,505bn and total assets of
₫627,365bn, against ₫630,893bn on the consolidated 2023Q2 quarterly. The separate
H1 (`ACB-20230814-b59e0d7d6112`) shares this form and is withheld. Effect on reads:
`pqt fundamentals ratios ACB --on 2023-08-11` gives book value per share 16,185.99
and P/B 1.41 (the quarterly). `--on 2023-08-16` gives 15,817.11 and 1.45 (the
separate H1, read as consolidated). This sits inside sprint 7's evidence window.
The PDF check above confirms that the form is not the consolidated statement. The
rule "the shared form is the consolidated one" does not hold for ACB in this period.

**Other separate reports that hold the consolidated form** have no consolidated
twin, because the twin was refused or never collected, so `SharedForm` never fires
for them. They have filled minority or parent-profit lines:
- MBB-20220127-d5ebd568fd02 (2021Q4, 700 = ₫2,911bn);
- POW-20230307-ab5f8838e0a0 (2022 annual);
- VCB-20230331-f0713d5716e3 and VIC-20230401-64446f39a2a2 (both 2022 annual,
  section 2);
- ACB-20250305-aeb6b673d868 (2024 annual);
- the two HPG reports of finding 1.

These values are recorded under the separate scope.

## 4. VPB: a 2025 form recorded as 2024Q4

IDS states the fiscal year off by one on two pairs of VPB filings:
- VPB-20250122-* has the abstract "Quý 4 năm 2024" and the 2024 form, labelled
  2025. These are refused.
- VPB-20260119-80ba8b15dfee and -a4b9517c66c3 have the abstract "Quý 4 năm 2025".
  Their form is labelled **2024** Q4 but holds 2025's figures: total assets
  ₫1,260,150bn, the same as the audited 2025 annual. They are **recorded as 2024Q4**,
  known from 2026-01-19. Nothing refuses a quarterly report filed 384 days after its
  period; it only counts as late.

Effect: `pqt fundamentals ratios VPB` reads profit quarters 2024Q4 to 2025Q3 on both
dates below, but the 2024Q4 figure changes when the misdated report arrives:

| session | net profit (TTM) | EPS (TTM) |
| --- | --- | --- |
| 2026-01-16 | ₫14,845bn | 1,871.12 |
| 2026-01-21 | ₫23,990bn | 3,023.72 |

The "2024Q4" profit after tax read from that report (bank line 21, ₫8,118bn) is 2025Q4's. This is outside dataset
`e1d22bee4937dad9`, which ends 2025-12-31, but every read from 2026-01-19 is wrong.
A check that the year in the abstract or name agrees with the form's year would
refuse the pair as `PeriodConflict` (proposed item P2).

## 5. The 111 dropped EPS lines

Each dropped value was divided by 10^6 and compared with an EPS implied by the same
form: profit to the parent ÷ (charter capital ÷ ₫10,000 par). A ratio near 1 means
a ×10^6 typo. Output: `tmp/audit-8-3/out/eps_check.tsv`.

| name | lines | ×10^6 typo? | note |
| --- | --- | --- | --- |
| VCB | 38 | yes | whole millions, ratio about 1 |
| VIB | 32 | yes | same |
| LPB | 14 | yes | same |
| BID | 2 | yes | same |
| TCB | 2 | yes | not whole millions (3,220,853,007), ratio about 1: EPS 3,220.85 × 10^6 |
| **GAS** | **20** | **no** | see below |
| **VRE** | **2** | **no** | 1,060,880,372 and 925,947,258 (H1 2025/2024 YTD). Implied EPS is about 530 and 440, so the value ÷ 10^6 is twice the EPS. Unexplained. Dropping it is right. |
| **SSB** | **1** | likely yes, not confirmed | 1,351,000,000 in the 2020Q4 quarter column of SSB's 2021Q4 form. ÷ 10^6 = 1,351, about SSB's full-year 2020 EPS, not a quarter's. |

**GAS: the income-statement lines are shifted one row down on five consolidated
quarterly forms.** The forms are GAS-20220429-eafecbfc6049, -20221028-fe110add5b3f,
-20230130-cb4708826d28, -20230428-6245c9318c1e and -20240426-ba25b2a787c4. On each:
- 61 (profit to the parent) is 0;
- 62 (non-controlling interest) holds the profit to the parent, for example
  ₫3,428.7bn in 2022Q1;
- 70 (basic EPS) holds the non-controlling interest: 66,673,770,718 = line 60 − line
  62, to the đồng, on all five;
- 71 (diluted EPS) holds the basic EPS.

So the 20 dropped lines are amounts of money, not EPS typos. Dropping them is right,
but the shift also corrupts the lines that are kept: **61 is recorded as a known 0.**
`StatementLines.Profit` reads 61 for a consolidated statement, so GAS's profit for
2022Q1, 2022Q3, 2022Q4, 2023Q1 and 2024Q1 is read as zero, with no absence code:

| session | profit quarters | net profit (TTM) | P/E |
| --- | --- | --- | --- |
| 2023-06-01 | 2022Q2–2023Q1 | ₫5,086bn, only 2022Q2; about ₫14,716bn with the shifted lines | — |
| 2024-11-01 | 2023Q4–2024Q3 | ₫8,566bn; about ₫11,077bn with 2024Q1 | 19.20; about 14.8 |

The 2024Q1 zero sits inside dataset `e1d22bee4937dad9` (2024-08-05 → 2025-12-31).
The same query over every Circular 200 report (`shift.sql`) finds only these five
GAS forms.

## Known gaps and proposed items

Lead's answers of 2026-10-21 are recorded here.

**Known gap: LPB's 11 unclassified quarterlies (2021Q4 to 2024Q2).** The refusal
stays, as the rule says. No PDF check in sprint 8. LPB has no quarterly values for
those periods.

**Proposed items, for Lead to triage with the room. No code in 8.3:**

| # | finding | proposal |
| --- | --- | --- |
| P1 | GAS: 5 consolidated forms with the income lines shifted; 61 recorded as a known 0 (section 5) | Detect the shift, or read 61 = 0 next to 62 ≈ 60 as absent rather than 0. Purge and re-import the five reports. |
| P2 | VPB: the 2025Q4 form recorded as 2024Q4 (section 4) | Refuse as `PeriodConflict` a filing whose abstract or name states a year other than the form's. Purge VPB-20260119-*. |
| P3 | ACB 2023 H1: the separate form recorded as consolidated (section 3; PDF confirmed) | Refuse, or withhold, a "consolidated" bank form with investment in subsidiaries (211) > 0. Purge ACB-20230814-7a97bb04ed90's values. |
| P4 | Separate reports holding consolidated-only lines (429, 61, bank 700) with no consolidated twin (section 3) | Decide whether to read them as the consolidated form, or to record them without values. |

**Data fix: HPG (finding 1).** Purge and re-import HPG's two separate 2022 reports.
This is Dev's, waiting for the Moderator.

## Reproduce

Run from `E:\PQT` in Git Bash, with the Docker stack up. All commands are read-only.

```sh
# 1. Mirror of the importer's refuse/withhold/drop rules (FilingClassifier,
#    StatementNormaliser, SharedForms at 3a6b1d5) over the collector files
PYTHONIOENCODING=utf-8 python -I tmp/audit-8-3/audit.py data/fundamentals/ids tmp/audit-8-3/out
# -> filings 1604 refused 22 withheld 692 shared 688 dropped 111
#    (withheld/shared include HPG's two new pairs; ADR-029's 686 + 4 was before 8.2)

# 2. Reports in the database, compared with the mirror (expect only the two HPG lines to differ)
docker compose exec -T postgres psql -U quant_user -d personal_quant -At -F$'\t' -c \
 "select source_reference, case when values_filed then 1 else 0 end, (select count(*) from quant.statement_lines l where l.report_id=r.id) from quant.financial_reports r order by 1" \
 | tr -d '\r' > tmp/audit-8-3/out/db3.tsv
tr -d '\r' < tmp/audit-8-3/out/recorded.tsv | sort | diff - tmp/audit-8-3/out/db3.tsv

# 3. Refusal detail (same-form siblings, indexed date, minority lines)
tr -d '\r' < tmp/audit-8-3/out/refused.tsv > tmp/audit-8-3/out/refused.lf.tsv
PYTHONIOENCODING=utf-8 python -I tmp/audit-8-3/refused_detail.py data/fundamentals/ids tmp/audit-8-3/out/refused.lf.tsv
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/periods.sql

# 4. Separate reports that hold consolidated-only lines
PYTHONIOENCODING=utf-8 python -I tmp/audit-8-3/separate_nci.py data/fundamentals/ids tmp/audit-8-3/out/recorded.tsv tmp/audit-8-3/out/db3.tsv
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/scope_fallback.sql

# 5. Shared form of the four banks: line 211, and total assets on one date
PYTHONIOENCODING=utf-8 python -I tmp/audit-8-3/subsidiary_line.py data/fundamentals/ids ACB SHB SSB VIB
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/same_date.sql

# 6. Dropped EPS lines against the implied EPS; GAS's line shift
tr -d '\r' < tmp/audit-8-3/out/dropped.tsv > tmp/audit-8-3/out/dropped.lf.tsv
PYTHONIOENCODING=utf-8 python -I tmp/audit-8-3/eps_check.py data/fundamentals/ids tmp/audit-8-3/out/dropped.lf.tsv
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/shift.sql
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/gas61.sql

# 7. VPB
docker compose exec -T postgres psql -U quant_user -d personal_quant < tmp/audit-8-3/vpb.sql

# 8. PDF spot check: fetch with installed Edge (3 s pause; writes only tmp/audit-8-3/pdf),
#    render pages to PNG with pdf.js 3.11.174 from jsDelivr; balance sheets are pages 7-8
PYTHONIOENCODING=utf-8 tools/ids-collector/.venv/Scripts/python.exe -I tmp/audit-8-3/fetch_pdfs.py tools/ids-collector data/fundamentals/ids tmp/audit-8-3/pdf
for f in tmp/audit-8-3/pdf/*.pdf; do PYTHONIOENCODING=utf-8 tools/ids-collector/.venv/Scripts/python.exe -I tmp/audit-8-3/render_pdf.py "$f" tmp/audit-8-3/pdf/png 1 10; done

# 9. Reads quoted above
CLI="MSYS_NO_PATHCONV=1 docker compose -f E:/PQT/docker-compose.yml exec -T backend dotnet cli/PersonalQuant.Cli.dll"
eval $CLI fundamentals ratios GAS --on 2023-06-01   # likewise 2024-11-01
eval $CLI fundamentals ratios ACB --on 2023-08-11   # likewise 2023-08-16
eval $CLI fundamentals ratios VPB --on 2026-01-16   # likewise 2026-01-21
```

`tmp/` is git-ignored. To keep the scripts, Lead may move them under
`docs/roadmap/evidence/`, as was done with `check_fundamentals_asof.py`.
