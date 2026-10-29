# ADR-038: Values for non-bank statements from fiscal 2026 — the IDS record dates them, a vendor supplies them

**Status:** Proposed (Lead, 2026-10-29; needs the Moderator's choice and verification 1 below) · **Date:** 2026-10-29 · **Phase:** Sprint 11, item 11.4 (Phase 7)

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
   finding, as ADR-029's trade-offs say.
4. **Restatement leak, stated and bounded.** A vendor may show the latest
   restatement against the first filing's date (ADR-029 trade-off). Every
   value carries `Vendor`, so a read can say what share of what it served was
   vendor-sourced, and the dataset's `fundamentals` object records the count.
   Where a later IDS form is filled (an issuer re-files, or IDS updates its
   form), the `as_filed` value becomes a revision, not an overwrite.
5. **Template mapping.** If Vietcap presents Circular 99 line codes, the five
   canonical items (revenue, profit to the parent, total assets, equity to the
   parent, basic EPS) are mapped once in `StatementLines` for the
   `NonFinancialCircular99` template, the same way the C200 and bank columns
   are mapped today. If it presents its own labels, the mapping is by label,
   kept in a fixture, and an unmapped item is absent, never zero.

### Verification before acceptance

1. **Vietcap's statement pages for one VN30 non-bank (FPT), fiscal 2026 Q1
   and Q2:** which statements, which line items and codes, which periods, and
   whether a value is the first filing or a restatement (compare FPT 2025Q1
   against its filled IDS form, which is exact to the đồng). Read-only, one
   ticker, a person's pace; no collector until this ADR is accepted. This is
   ADR-029's verification 1 (Vietcap column) and verification 3, deferred
   there and now needed.

If Vietcap exposes no fiscal-2026 non-bank statements, the fallback is the
alternative below that the Moderator picks, and this ADR is rewritten.

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
  verification; recorded as `as_filed` with a `hand_keyed` finding, never
  silently. Not chosen as the standing source.
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
  `vendor_rows` count. The `NonFinancialCircular99` comment and ADR-029 say
  "a 2026 C200 form left empty" (Dev's small docs PR).
- Collection: a vendor read for the 19 non-bank VN30 names, fiscal 2026
  onward, paced; the Moderator approves the first import, as for every live
  write.
- Tests: the join refuses a vendor filing with no IDS record; a vendor value
  never overwrites an `as_filed` one; an unmapped C99 item is absent; a read
  reports its vendor share.
- Phase 7's report states the vendor share of each value feature's inputs.
