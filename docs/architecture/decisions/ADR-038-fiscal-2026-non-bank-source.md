# ADR-038: Values for non-bank statements from fiscal 2026 — the IDS record dates them, a vendor supplies them

**Status:** Proposed (Lead, 2026-10-29; QA + QC review applied: 1 blocker, 5 minor; needs the Moderator's choice and verifications 1 and 2 below) · **Date:** 2026-10-29 · **Phase:** Sprint 11, item 11.4 (Phase 7)

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

## Decision (proposed)

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

### 2 · Fidelity · partial, FPT only

4 statements × 5 items: **11 match to the đồng, 9 differ.** FPT 2025Q1 and
2025Q3 are 10 of 10 exact. Every 2024Q2 and 2024Q4 balance-sheet difference
equals the later restated IDS reading (the reviewed half-year, the audited
annual), and both profit differences equal the next year's comparative.
**Vietcap always shows the latest restated value, never the first.** EPS is
unreliable: 2 of 4 differ from every IDS reading. HPG and VNM are not yet read
(see the read rule below), so the 12-statement sample is not complete.

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
  bounds and this ADR falls back to the hand-keyed stop-gap.

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
