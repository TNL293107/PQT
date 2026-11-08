# ADR-038: Values for non-bank statements from fiscal 2026 — the IDS record dates them, a vendor supplies them

**Status:** Accepted. Drafted by Lead 2026-10-29; QA + QC review applied (1 blocker, 5 minor). **Source chosen by the Moderator on 2026-11-02: Vietcap, option (c).** Verification 2 completed 2026-11-02 (12 statements, 3 names). **Accepted** (Lead, 2026-11-08, after QA + QC review of the verification) · **Date:** 2026-10-29 · **Phase:** Sprint 11, item 11.4 (Phase 7)

## Context

Sprint 11's investigation (`tmp/c99-investigation/README.md`, Dev,
2026-10-29, read-only) settled what the 99 "Circular 99" reports are. They are
**not a new form**. The 2026 IDS filings carry the Circular 200 layout of 2025,
row for row, with every cell `"0"`: issuers attach the signed PDF and leave the
structured form blank, because the statements are drawn up under Circular 99
and the IDS form was not updated. The backend's `NonFinancialCircular99` label
is an inference from "fiscal 2026 and no values"
(`StatementNormaliser.TemplateOf`), not a reading of a different form. There is
nothing to parse and nothing to map.

ADR-029 saw this coming (verification 4: "stored as *disclosed, values not
filed* until a source for Circular 99 statements is chosen"). The consequence
is now measurable: 19 of VN30's non-bank issuers have no balance sheet or
profit after 2025Q4, and in the 9.1 build 14 of 26 names read a 2025Q4 balance
sheet at 2026-09-17. `size`, `value_ep`, `value_bp` and `quality_roe` are up to
nine months stale for every non-bank and get staler each quarter. Banks are not
affected: all 35 fiscal-2026 bank reports with values are on the bank form.

What ADR-029 already decided and what exists:

- `reported_at` comes from the disclosure record (decision 1). **The blank
  2026 filings are still disclosure records**: they date the fact.
- Values carry a declared basis (decision 3): `as_filed` or `vendor`.
  `ValueBasis.Vendor` exists in the domain; no provider writes it yet.
- Vietcap publishes written terms permitting personal download and use; its
  chart endpoint is already read by the ADR-015 adapter (ADR-029 §licence).
- HOSE sells its disclosures: no individual tier, and the Excel statements
  alone are 120,000,000 ₫ a year (ADR-029, fee schedule read 2026-10-13).
  Buying is outside the operator's constraint.
- Extracting numbers from the filed PDFs was rejected in ADR-029: many are
  scans, and OCR on financial figures breaks the exact-decimal rule.

## Decision

**For non-bank statements from fiscal 2026, the IDS filing keeps dating the
fact and a vendor supplies the numbers, labelled `vendor`, exactly as ADR-029
decision 3 provides. Nothing else about the model changes.**

1. **The disclosure record is unchanged.** A 2026 non-bank period is known
   from its IDS filing date (decision 2's next-session rule). A period with a
   vendor value and no IDS filing is not a fact; it stays a finding.
2. **A second `IFundamentalsProvider`, `Basis = Vendor`.** It supplies the
   same `ProviderFiling` shape (tables of labelled, coded rows as text), keyed
   by ticker, scope and period, so the importer and normaliser are reused.
   Selection stays explicit: an import names the provider, and nothing falls
   back. The vendor is Vietcap, the only candidate with written personal-use
   terms, read at a person's pace for VN30 names only, as ADR-015 bounds it.
3. **Join rule.** A vendor filing attaches to the IDS report of the same
   ticker, scope, fiscal year and period. The report's `ValuesFiled` stays
   false (the form was blank); its lines carry `Vendor`. A mismatch is a
   finding, as ADR-029's trade-offs say. "The same period" includes the
   period type: a vendor Q2 attaches to the IDS Q2 report and an H1 to the H1
   report (the 2026 filings are 64 quarterly and 35 half-year), and a vendor
   period with no IDS report of that type is a finding, never attached to a
   neighbour. `reported_at` is always the IDS report's date; a vendor's own
   date is never used.
4. **Restatement leak: exposure and rate, measured separately.** A vendor
   may show the latest restatement against the first filing's date (ADR-029
   trade-off). Two different quantities:
   - the **exposure**, the share of served values that are vendor-sourced:
     every value carries `Vendor`, so a read reports it, and the dataset's
     `fundamentals` object records the count;
   - the **leak rate**, how often a vendor value is a restatement rather than
     the first filing: it comes from verification 2's sample, not from any
     count in the store.

   Where a later IDS form is filled (an issuer re-files, or IDS updates its
   form), the `as_filed` reading supersedes the vendor line as a revision,
   **even when the value is equal**: a change of basis from `Vendor` to
   `AsFiled` is a revision, not a no-op (`StatementLine.Restate` changes the
   value or the basis).
5. **Template mapping.** The five canonical items (revenue, profit to the
   parent, total assets, equity to the parent, basic EPS) are mapped once in
   `StatementLines` for the `NonFinancialCircular99` template, the same way the
   C200 and bank columns are mapped today. Verification 1 decides whether the
   key is Vietcap's line codes or its labels, and only that one is built. An
   unmapped item is absent, never zero.

### Verification before acceptance

Read-only, at a person's pace; no collector until this ADR is accepted.

1. **What Vietcap presents** for one VN30 non-bank (FPT), fiscal 2026 Q1 and
   Q2: which statements, which line items with their codes or labels, which
   periods (quarter and half-year), consolidated or separate, and the units.
   This is ADR-029's verification 1, Vietcap column.
2. **Fidelity, ADR-029's verification 3**, which this decision now depends
   on: 12 filled fiscal-2024 and 2025 statements, across at least 3 non-bank
   names and several quarters, compared line by line on the five canonical
   items against their IDS forms (exact to the đồng). Report n differ out of
   n total and the reason for each difference (restatement, rounding, units,
   scope). This is the leak rate in §4; one name and one quarter is not
   enough to accept on.

If Vietcap exposes no fiscal-2026 non-bank statements, the fallback is the
alternative below that the Moderator picks, and this ADR is rewritten.

## Verification record

### 1 · What Vietcap presents · 2026-10-30 (Dev, read-only)

`tmp/adr-038-verification/NOTES.md`. `GET iq.vietcap.com.vn/api/iq-insight-service/v1/company/{ticker}/financial-statement?section=INCOME_STATEMENT|BALANCE_SHEET|CASH_FLOW|NOTE`,
no account. One row per period: `data.quarters` (`lengthReport` 1–4, the
quarter alone) and `data.years`. **No half-year row, and no scope field**: the
values are consolidated only. Fields are Vietcap's own (`isa*`, `bsa*`), not
Circular 200 or 99 codes, named in a `metrics` dictionary. The five items:
`isa3` net sales, `isa22` profit to the parent, `isa23` basic EPS, `bsa53`
total assets, `bsa78` equity including minorities. Units VND, as JSON numbers
(to be parsed as decimals). FPT has 2018Q1 to **2026Q3**, with 2026Q1–Q3
filled: the gap this ADR exists for is covered for FPT.

**One value per period, the latest.** `updateDate` moves (FPT 2026Q1's balance
sheet was updated five weeks after `publicDate`), and no earlier reading is
kept.

### 2 · Fidelity · 2026-11-02 (Dev, read-only), complete

4 statements × 5 items: **11 match to the đồng, 9 differ.** FPT 2025Q1 and
2025Q3 are 10 of 10 exact. Every 2024Q2 and 2024Q4 balance-sheet difference
equals the later restated IDS reading (the reviewed half-year, the audited
annual), and both profit differences equal the next year's comparative.
**Vietcap always shows the latest restated value, never the first.** EPS is
unreliable: 2 of 4 differ from every IDS reading.

HPG and VNM were then read with the project's own agent
(`PersonalQuantTerminal/0.1 (+research)`, no `Referer` or `Origin`), after
the Moderator approved the reads; **all four answered 200**, so the endpoint
needs no browser identity. The full sample is **12 statements over 3 names,
60 items** (FPT 2024Q2, 2024Q4, 2025Q1, 2025Q3; HPG 2024Q1, 2025Q1, 2025Q2,
2025Q4, with 2025Q1 standing in for an HPG 2024Q3 that IDS does not hold;
VNM 2024Q2, 2024Q4, 2025Q1, 2025Q3), in `tmp/adr-038-verification/`.

| Name | Items that differ | Explained by a later IDS reading |
| --- | --- | --- |
| FPT | 9 / 20 | 6 (2024Q2 and 2024Q4: lines 270 and 400 = the half-year review or the audited annual, line 61 = the next year's comparative; `fpt-later-readings.txt`) |
| HPG | 4 / 20 | 1 (2025Q4 total assets = the audited annual) |
| VNM | 4 / 20 | 2 (2024Q4 total assets and equity = the audited annual) |

**17 of 60 items differ; 7 of 12 statements match on all five items** (FPT
2025Q1 and 2025Q3; HPG 2024Q1 and 2025Q1; VNM 2024Q2, 2025Q1 and 2025Q3). The
leak rate of §4 for this sample: **9 of the 17 differences are restatements**
the vendor shows against the first filing's date (FPT 6, HPG 1, VNM 2). The
other 8: EPS 3, HPG profit 2, VNM placeholders 2, FPT 1 đồng. Counted by
script from `fidelity-all.txt`; its matcher checked later readings for HPG
and VNM only, so FPT's restatements come from `fpt-later-readings.txt`.

- **EPS** differs from every IDS reading on 3 statements (FPT 2024Q2 and Q4,
  HPG 2025Q4, where HPG files line 70 as 0). EPS is computed, never read
  (decision below).
- **Profit to the parent** differs on 2 of 12 statements (HPG 2025Q2 and
  2025Q4) and equals no IDS reading. This is why a vendor value stays its own
  basis and never overwrites an `as_filed` one.
- **VNM 2024Q4's IDS form holds placeholders**: revenue exactly
  20,000,000,000,000 and profit exactly 2,000,000,000,000. Vietcap's
  15,477,073,125,441 and 2,123,649,268,482 are the real figures. Here the
  filed form is wrong and the vendor right. A filed placeholder is an
  `as_filed` value PQT stores today; finding such values is a data-quality
  item of its own, recorded in the Phase 6 backlog table
  (`docs/roadmap/phase-6-definition-of-done.md`), not part of this ADR.
- FPT 2024Q4 revenue differs by 1 đồng.

### Consequences of the record for the decision

- **Dated at fetch, never at `publicDate`.** Because a restated figure carries
  no date of its own, a vendor line is recorded as a reading observed when it
  was fetched. It never back-dates to the vendor's `publicDate`. With
  decision 1 that means a 2026 non-bank value is known from the later of the
  IDS filing date and the fetch.
- **EPS is computed, not read.** `isa23` is not used; EPS comes from profit to
  the parent and the share count, as the per-share ratios already do.
- **Half-years.** With no half-year row, the 35 half-year IDS reports get no
  vendor values; the quarters carry the year.
- **Read rule.** Every read identifies itself as the ADR-015 adapter does
  (`PersonalQuantTerminal/0.1 (+research)`, no browser `User-Agent`, no
  `Referer`). If the endpoint answers only a browser, Vietcap fails ADR-015's
  bounds and this ADR falls back to the hand-keyed stop-gap. Verification 2
  showed it answers the honest agent. Reads are at least 10 seconds apart (`Crawl-delay: 10`, see
  the terms and legal review).

## Terms and legal review · 2026-11-08

Read on the Moderator's request before the first import. **This is a reading of
texts by the room, not legal advice**; the open points at the end need a
Vietnamese lawyer. "Read" means the text was fetched and read; "cited" means it
was seen only in commentary or search results and must be checked on vbpl.vn or
congbao.chinhphu.vn.

### Vietcap's terms of use (read)

`https://www.vietcap.com.vn/dieu-khoan-su-dung-chinh-sach-bao-mat`, read
2026-11-08 by Lead and re-read the same day by QA + QC. The terms of use carry
no date or version; the privacy policy on the same page postdates 31/12/2025
(it cites Nghị định 356/2025/NĐ-CP of that date) while the footer says ©2023,
so the terms may be older than the policy beside them.

- **Scope:** "trang web này" (this website); the rule below names
  `trading.vietcap.com.vn` itself. It does not name Vietcap IQ, but
  `iq.vietcap.com.vn` now redirects to `trading.vietcap.com.vn/iq`, so the IQ
  pages sit under the domain the terms name. Terms shown only inside the
  JavaScript application could not be read.
- **The rule, one sentence:** "Ngoại trừ việc tải xuống và in các thông tin
  được yêu cầu để sử dụng cho mục đích cá nhân, Người sử dụng không được phép
  sao chép, dẫn liên kết: https://trading.vietcap.com.vn, phân phối, kinh doanh
  hoặc xuất bản các nội dung của trang web dưới bất kỳ hình thức nào." The
  **ban is the rule** (no copying, linking to trading.vietcap.com.vn,
  distributing, trading or publishing the content in any form), and
  downloading and printing for personal use is the **only exception** to it,
  not a standalone grant. Open point 1 turns on that exception. All rights are
  reserved.
- **Automated access:** not mentioned. No clause permits or forbids scraping,
  bots or programmatic access.
- **`robots.txt`** on `trading.vietcap.com.vn` (which covers `/iq`):
  `User-agent: * / Allow: / / Crawl-delay: 10`.

### Vietnamese law (summary of a research pass)

**For a "cited" row, the status is what commentary reports, unverified**: it is
not a checked fact until read on vbpl.vn or congbao.chinhphu.vn.

| Text | Status found | Basis |
| --- | --- | --- |
| Thông tư 99/2025/TT-BTC | Reported: issued 27/10/2025, in force 01/01/2026, for financial years from that date; replaces Thông tư 200/2014 (not 133/2016) | cited |
| Thông tư 96/2020/TT-BTC (disclosure) | In force (2020 text read). Reported amended by Thông tư 68/2024, 18/2025 and 08/2026 (consolidated 10/VBHN-BTC 2026); deadlines in Điều 14 reported unchanged | 2020 text read; amendments cited |
| Quyết định 450/QĐ-UBCK (IDS) | Reported superseded: 245/QĐ-UBCK (2024), 877/QĐ-UBCK (2024), then 820/QĐ-UBCK (23/10/2025), which reportedly names the portal `ids.ssc.gov.vn` | cited |
| Luật Sở hữu trí tuệ (as amended 2022, and 131/2025/QH15 from 01/04/2026) | Điều 15: no copyright in bare data (*số liệu*). Điều 22: a data collection is protected only for a creative selection or arrangement. No separate database right was found. Điều 25: one copy for personal, non-commercial research | Điều 22 read; others cited |
| Luật Chứng khoán 54/2019 (amended 56/2024), Nghị định 155/2020 | Nothing found that restricts an individual collecting public disclosures for private use | partly read |
| Bộ luật Hình sự Điều 289 (unauthorised access) | Requires overcoming warnings, passwords or firewalls, "or other means", plus taking control, interfering or taking data | cited |
| Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15, Nghị định 356/2025/NĐ-CP | In force 01/01/2026; personal data only | cited |
| Luật An ninh mạng 116/2025/QH15 | From 01/07/2026; its collection ban concerns personal data | cited |
| Luật Dữ liệu 60/2024/QH15 | **Not checked** | — |

No text was found that makes reading publicly accessible data unlawful in
itself, in the texts checked; Luật Dữ liệu 60/2024/QH15 was not checked.

### What this changes in the decision

- **Pacing: at least 10 seconds between reads**, as `Crawl-delay: 10` asks.
  The verification reads (1.5–2 s apart) were faster than that; the provider
  is not.
- **What is stored stays minimal:** the five canonical numbers per period, and
  the raw response for audit. Bare figures carry no copyright (Điều 15);
  Vietcap's presentation is not reproduced, and nothing is published,
  distributed or sold.
- **Personal data:** statements can carry officers' or shareholders' names.
  The provider stores none of them.
- **ADR-029 cites Quyết định 450/QĐ-UBCK as current; it is reported
  superseded by 820/QĐ-UBCK (2025)** (cited, unverified). ADR-029's reasoning
  is unchanged; a note is added there.

### Open: needs a Vietnamese lawyer before the first import

1. Whether keeping a local copy of the numbers is "tải xuống … để sử dụng cho
   mục đích cá nhân" (permitted) or "sao chép" (forbidden) under Vietcap's
   terms, and whether those terms bind as a contract.
2. Whether calling an undocumented API with no login, at the robots.txt pace,
   can fall under Điều 289's "other means".
3. Whether Luật Dữ liệu 60/2024 reaches this use.
4. The current consolidated texts of 96/2020 and 820/2025.

**Gate:** the first import runs only on the Moderator's explicit OK, given with
these open points in view.

## Alternatives

- **HOSE's public disclosure listing.** Free to read, but it lists filings and
  links the PDFs; it carries no structured values for a non-bank period
  either. It would improve `reported_at` (times to the second), not fill the
  gap. Kept for the date, not chosen for the values.
- **HOSE's paid package.** Answers the question outright, with Excel
  statements. 120,000,000 ₫ a year, no individual tier. Excluded by the
  operator's constraint; if that changes, this ADR is superseded.
- **Parse the filed PDFs.** Rejected in ADR-029 (scans, exact decimals). It
  stays what it was: a manual cross-check of a vendor value, never a pipeline.
- **Hand-key the five canonical items from the PDFs** for 19 names × 4 periods
  a year. Exact and licence-clean, about 400 numbers a year, each a possible
  typo. Viable as a stop-gap for one or two quarters if the vendor fails
  verification, under a fixed procedure: every number is read twice by
  independent readings (or once and checked against a vendor value where one
  exists); it is recorded as `as_filed` with a `hand_keyed` finding, never
  silently; and it is superseded the moment an `as_filed` form value or a
  verified vendor value arrives. Not chosen as the standing source.
- **Leave the gap.** Phase 7's value and quality factors go blind for every
  non-bank from 2026, and so do Phases 8–10. Rejected.

## Trade-offs

- **The restatement leak is real for these periods** (decision 4). It is
  labelled and measurable, not eliminated.
- **A second source to keep collecting.** The vendor read runs beside the IDS
  collector, under the same pacing and personal-use bounds.
- **Licence footing is the vendor's written personal-use grant.** Local,
  personal, never redistributed; if PQT ever earns revenue, this reopens
  first, as ADR-015 and ADR-029 already say.

## Consequences

- Backend: `VietcapStatementProvider` (`Basis = Vendor`), its own
  `SourceCode`; the join in `FundamentalsImportService`; the C99 mapping in
  `StatementLines`; the dataset manifest's `fundamentals` object gains a
  `vendor_rows` count. That count enters the content hash, so it is
  **manifest schema 6**, hashed from schema 6 only (the rule of ADR-031 and
  ADR-034), with C# and Python parity fixtures as for schema 5. The `NonFinancialCircular99` comment and ADR-029 say
  "a 2026 C200 form left empty" (Dev's small docs PR).
- Collection: a vendor read for the 19 non-bank VN30 names, fiscal 2026
  onward, paced; the Moderator approves the first import, as for every live
  write.
- Tests: the join refuses a vendor filing with no IDS record, and one whose
  period type has no IDS report; a vendor value never overwrites an
  `as_filed` one; an `as_filed` reading of an equal value supersedes the
  vendor line as a revision; an unmapped C99 item is absent; a read reports
  its vendor share.
- Phase 7's report states the vendor share of each value feature's inputs.
