# ADR-029: Fundamentals source — the disclosure record says when, the filing says what

**Status:** Proposed · **Date:** 2026-10-13 · **Phase:** Sprint 6, Phase 6 (point-in-time fundamentals)

> **Proposed, not accepted.** The operator approves the source and its licence
> before any import (sprint plan, operator decisions). Until then nothing is
> fetched beyond the public pages read to write this record, and the four
> verifications at the end are done, in order, before the status changes.

## Context

Phase 6 stores financial statements so that factor research (Phase 7) can read
value, quality and earnings as they were *known*, not as they are now. The
binding requirement is in [`system-context.md`](../system-context.md): each
fact carries the fiscal period it describes **and** the moment it became
public. A source with only the period cannot support an honest backtest,
because a Q4 profit read on 1 January was not public until February or March.

The operator's constraint from [ADR-015](ADR-015-vietnam-market-data-provider.md)
still holds: free, continuously updatable, no broker account. That rules out
the commercial point-in-time products before their merits are weighed.

What the Vietnamese disclosure regime gives, checked against the source texts:

- **Deadlines** (Circular 96/2020/TT-BTC, as tabulated in Appendix I of
  Decision 450/QĐ-UBCK): audited annual statements within 10 days of the audit
  report and no later than 90 days after year end. Reviewed semi-annual
  statements within 5 days of the review report and no later than 45 days.
  Quarterly statements within 20 days of quarter end, and 30 days for a parent
  company with its own accounting function. Where a quarter is reviewed, within
  5 days of the review report and no later than 45 days.
- **Deadlines are not met reliably.** HOSE publicly reminded Vinh Hoan that its
  Q1 2024 statements had not arrived by 2 May 2024 despite an extension, and it
  granted SMC an extension of its 2024 audited statements to 29 April 2025. A
  deadline is therefore not an upper bound on when a fact became public.
- **Where filings are lodged.** Public companies file on the State Securities
  Commission's Information Disclosure System (IDS). Under Decision
  450/QĐ-UBCK (2021) the IDS has two parts. Filings are submitted at
  `ids.ssc.gov.vn` and published at `congbothongtin.ssc.gov.vn`. The decision
  distinguishes *ngày báo cáo*, the day IDS received a filing, from *ngày công
  bố thông tin*, the day it appeared. Financial statements are entered through
  the IDS's own **Excel or XBRL forms**, which give structured line items, with
  the signed full text attached as a PDF.
- **One point of disclosure since 15 August 2024.** Companies listed on HOSE
  stopped filing periodic disclosures on IDS and file on HOSE's system instead.
  For a HOSE name, the record of *when* is IDS before that date and HOSE after
  it. Most of VN30 is on HOSE.
- **Granularity.** The SSC portal lists each filing with its submission date
  (*Thời gian gửi*) and no time of day.
- **Templates differ by industry and by year.** Non-financial companies, banks,
  securities firms and insurers report on different statement forms. The
  enterprise accounting regime changes on 1 January 2026, when Circular
  99/2025/TT-BTC replaces Circular 200/2014. Half of VN30 are banks.

## Decision (proposed)

**A fundamental fact enters PQT only when a disclosure record dates it. The
record comes from the regulator or the exchange, never from a deadline and
never from a vendor's say-so. The numbers come from the filing's own structured
form where it can be read, and otherwise from an aggregator, labelled as such.**

1. **`reported_at` comes from a disclosure record.** For filings before 15
   August 2024, and for HNX and UPCOM issuers, the record is IDS
   (`congbothongtin.ssc.gov.vn`). For HOSE issuers after that date, it is
   HOSE's disclosure system. A statement with no disclosure record is not
   stored as a fact. It is a finding: *period ended, no disclosure found*.
2. **Day granularity is read conservatively.** A filing dated D with no time
   is known from the start of the next session after D. The record cannot say
   whether it arrived before or after the close. A record that carries a time,
   as HOSE's do (verification 1), is known from the first session that opens
   after that time.
3. **The numbers have a declared basis.**
   - `as_filed` means read from the filing's structured IDS form (Excel or
     XBRL), which is the issuer's own submission.
   - `vendor` means read from an aggregator: the Vietcap feed already reached
     by the ADR-015 adapter, with no third-party client.

   The aggregator is accepted only for the numbers. Its values may be the
   latest restatement, not the first filing. So each value records its basis,
   and each fact keeps its revisions. An `as_filed` observation that disagrees
   with a `vendor` value becomes a revision, not an overwrite, as `bar_revisions`
   does for prices.
4. **Statutory deadlines are a check, never a timestamp.** A disclosure dated
   after its deadline is noted as late. A period past its deadline with no
   disclosure is reported as missing, and nothing is imputed.
5. **Line items are mapped per template and accounting regime.** The five
   templates are TT200 non-financial, TT99 non-financial, bank, securities
   firm and insurer. Each canonical item that a ratio needs is mapped from each
   template. An unmapped item is absent, not zero.
6. **The first import is narrow.** VN30 names, quarterly, from 2022, per
   sprint plan item 6.5. Widening is a separate decision.

## The seven questions, per candidate

Answered from published texts. Every *VERIFY* cell is answered in writing,
here, before the status moves to Accepted.

| # | Question | SSC IDS portal | HOSE disclosure | Vietcap feed (aggregator) |
| --- | --- | --- | --- | --- |
| 1 | Availability | Public web portal, no account. Filings filterable by venue, ticker, report type, year, quarter | Public web pages on hsx.vn. **VERIFY** the listing and its fields | Undocumented endpoints behind Vietcap's web front end, as in ADR-015. **VERIFY** which statements and fields exist |
| 2 | Underlying source | The issuer's own filing, received by the regulator | The issuer's own filing, received by the exchange | The issuer's filing, re-keyed or re-processed by the broker |
| 3 | Data ownership | The issuer's content, disclosed under statute, published by SSC | As IDS, published by HOSE | The issuer's content. The broker's presentation |
| 4 | Licence / terms | No terms published (verification 1). Footer reads "Copyright © 2019 - SSC". No `robots.txt` | No terms page. `robots.txt` disallows nothing. **HOSE sells its disclosures as a paid information product** (verification 1) | **Written terms exist.** Personal download and printing are permitted. Copying, distributing, trading and publishing are not (verification 1) |
| 5 | Storage rights | Not granted in writing. These are statutory public disclosures, the strongest footing available for retaining a copy for personal research | Not granted for the public pages. A feed of the same content is licensed for a fee | Granted for personal use, in writing. Nothing wider |
| 6 | Redistribution | Not granted, not exercised | Not granted, not exercised | Not granted, not exercised |
| 7 | Revision behaviour | A corrected filing is a new filing with its own date: history is appended, not rewritten. **VERIFY** on one known re-filing | As IDS. **VERIFY** | **Unknown, assumed silent rewrite.** That is why the basis is declared and the disclosure record is required |

## Alternatives

- **The statutory deadline as `reported_at`.** It is free and complete, and it
  is wrong exactly when it matters. Late filers and extensions put a fact into
  a backtest before it was public. The Vinh Hoan and SMC cases above are from
  2024–25, not hypothetical. It is kept as a check (decision 4).
- **A fixed lag**, such as treating everything as known 90 days after period
  end. Late filers break it in the same way, and it discards the timeliness
  that most quarterly filers provide.
- **An aggregator alone.** It has the period and not the moment, so it fails the
  binding test. It is the rejection the sprint plan anticipates.
- **Extracting the numbers from the filed PDFs.** Many filings are scans. OCR on
  financial figures breaks the exact-decimal rule that prices already follow
  (ADR-015, parsing rule). Kept as a manual cross-check, not a pipeline.
- **A commercial point-in-time product** (FiinPro-X, LSEG, S&P Capital IQ).
  These are the only sources with recorded first-reported values and their
  revisions, so they would answer decision 3 outright. They are excluded by the
  operator's constraint, not on merit. If the constraint changes, this ADR is
  superseded.
- **`vnstock`'s finance module.** Rejected for the same reasons as in ADR-015:
  licence containment, parsing and layering.

## Reasoning

The binding test asks two questions, and no free source answers both. The
regulator and the exchange answer *when*, because their records are the
disclosure: a statement was public once it appeared there, by definition.
Answering *what* is easier from an aggregator and more faithful from the
filing. Splitting the two keeps the part that makes backtests honest, the date,
on the source that cannot be wrong about it. The part that can drift, the
values, gets a declared basis and a revision history, so drift is visible.

This is the same shape as corporate actions under ADR-022. A fact without its
announcement moment is not used in a strict read, and a read reports what it
withheld. Fundamentals take the stricter form: without a disclosure record
there is no fact at all. A strict read that silently lacked the dates would
look like a working backtest while measuring the future.

## Trade-offs

- **Two sources must be joined.** A filing on IDS or HOSE is matched to a
  period's values by ticker, statement type (separate, consolidated or parent),
  period and year. A mismatch is a finding. This is the main new failure
  surface.
- **The `vendor` basis can leak a restatement.** If the aggregator shows a
  restated Q2 value against Q2's original filing date, a backtest sees the
  restatement early. The basis label makes the exposure measurable. The third
  verification measures its size. It is not eliminated unless every value is
  `as_filed`.
- **Day granularity costs up to one session of timeliness.** A filing made at
  09:00 is treated as known only from the next session.
- **Storage rests partly on an absence of terms.** Vietcap grants personal use
  in writing. IDS and HOSE publish statutory disclosures with no terms either
  way, which is firmer ground than a broker's chart feed but is still not a
  written permission. HOSE also sells the same stream (verification 1). The
  bounds of ADR-015 apply: personal research, local, never redistributed, and
  if PQT ever earns revenue, this reopens first.
- **The 2024 switch splits each HOSE name's history across two systems,**
  which means two adapters. If either changes or disappears, the dates for
  that stretch become unobtainable.

## Verification before acceptance (in this order)

1. **Terms.** Capture the IDS portal's consent dialog, any hsx.vn terms page and
   Vietcap's site terms. Record each in this ADR under a dated heading. If any
   forbids retention, that candidate is dropped and this ADR is rewritten
   before going further. **Done 2026-10-13**, below. HOSE's fee schedule is
   still to be read.
2. **Disclosure coverage.** For one non-financial name, one bank and one
   securities firm in VN30, and for each quarter from Q1 2022 on, confirm a
   disclosure record and its date. Use IDS before 15 August 2024 and HOSE
   after. Confirm whether the IDS listing offers the structured Excel/XBRL
   form for download, or only the PDF.
3. **Vendor fidelity.** For twelve of those statements, compare the Vietcap
   values line by line against the filed PDF. Record how many differ and why.
   This decides whether `vendor` values are first-filed in practice, or
   whether the leak in the trade-offs is real.
4. **Templates.** List the line items each of the five templates carries for
   the canonical items Phase 7 needs: revenue, net profit attributable to the
   parent, total assets, equity attributable to the parent, and shares
   outstanding. Record the mapping.

## Verification 1 · terms · 2026-10-13

Done by reading each site's own pages in a browser. No data endpoint was
called and no file was downloaded.

**SSC disclosure portal (`congbothongtin.ssc.gov.vn`).** The portal publishes
no terms of use and links to none. The only legal text is the footer,
"Copyright © 2019 - SSC - Version 1.7". The two *Đồng ý* buttons noted in the
draft are the empty message and utility dialogs of the Oracle ADF framework
the portal is built on. They have no body text and are not a consent to terms.
`/robots.txt` returns 404, so the portal declares no crawling restriction. The
SSC's main portal (`ssc.gov.vn`) could not be read with the tools available
here, so whether it publishes a site-wide notice is still open.

**HOSE (`hsx.vn`).** The site links to no terms of use. `/robots.txt` reads
`User-agent: * / Disallow:` (nothing disallowed). However, HOSE runs a paid
*Dịch vụ cung cấp thông tin* (information service, page dated 26/03/2024
16:43:00, with a fee schedule attached). It sells, among other packages,
*Thông tin công bố*: the statutory disclosures of listed companies. It is
offered to organisations and individuals, by leased line, internet, agents,
email or FTP. HOSE therefore treats a *feed* of its disclosures as a product it
licenses. The public pages carry no terms forbidding personal reading, but a
systematic copy of the disclosure stream is closer to that product than to
reading a page.

The disclosure listing (*Tin tổ chức niêm yết*) carries a creation date and a
time to the second for each item, for example `13/10/2026 17:31:15`. After 15
August 2024, HOSE dates are therefore finer than IDS dates.

**Vietcap (`vietcap.com.vn`).** Unlike what ADR-015 §4 recorded, Vietcap
publishes terms (*Điều khoản sử dụng*). Under *Quyền sở hữu trí tuệ*, Vietcap
reserves all rights in the site's information. It permits downloading and
printing for personal use, and otherwise forbids copying, linking,
distributing, trading or publishing the content in any form. The terms name
the trading site `trading.vietcap.com.vn`, which is the host of the endpoints
ADR-015's adapter calls.

**Result: no candidate forbids retention for personal research, so none is
dropped.** The consequences:

- **Vietcap's position is firmer than ADR-015 recorded, and narrower.** Personal
  use is granted in writing. Copying beyond it, publication and any commercial
  use are refused in writing, not merely ungranted. PQT's existing bounds
  (local, personal, never redistributed) already sit inside the grant. ADR-015
  is not edited. This record is the pointer.
- **HOSE needs one decision before an adapter is built.** Choose either:
  - read the public listing at a personal-research rate, for VN30 names only,
    keeping the title, the timestamp and the link but not the documents; or
  - buy the disclosure package if the fee schedule has a tier an individual
    can afford.

  The fee schedule is a PDF on hsx.vn, `20240326_Bieu gia dich vu thong tin ap
  dung tai So GDCK TPHCM.pdf`. It has not been downloaded, pending the
  operator's permission.
- **Decision 2 can be finer for HOSE.** Where a record carries a time, the
  fact is known from the first session that opens after it. A day-only record
  keeps the next-session rule.

## Consequences

- **Sprint 6 item 6.2.** A fact has an instrument, a fiscal period, a statement
  scope (separate, consolidated or parent), a template, a line item, a value
  with its basis, and `reported_at` from its disclosure record. Revisions are
  append-only.
- **Sprint 6 item 6.3.** Two provider kinds sit behind the boundary: a
  disclosure-record provider (IDS, then HOSE) and a statement-values provider
  (IDS forms, then Vietcap). Raw payloads are retained, as for bars. Contract
  tests run against recorded responses, never a live endpoint in CI.
- **Sprint 6 item 6.4.** The as-of read joins facts on `reported_at`. It
  reports how many values it served by basis, as dataset reads already report
  `adjustmentsWithheld`.
- **The coverage registry's Fundamentals row** measures periods disclosed,
  periods with values, and periods past deadline with nothing. It does not
  count rows.
- Nothing fetched is committed. Recorded test responses are synthetic or
  trivially small, per [`../data-policy.md`](../data-policy.md).

## Sources read for this record

- Decision 450/QĐ-UBCK (14 July 2021), *Quy chế sử dụng Hệ thống công bố thông
  tin của UBCKNN*: Articles 3, 6 and 10 and Appendix I. Copy:
  <https://mbs.com.vn/files/uploads/2025/04/450_qd-ubck_quy-che-su-dung-he-thong-cbtt-cua-ubcknn.pdf>
- SSC disclosure portal, filing listing:
  <https://congbothongtin.ssc.gov.vn/faces/NewsSearch>
- One point of disclosure on HOSE from 15 August 2024:
  <https://thitruongtaichinhtiente.vn/ap-dung-cong-bo-thong-tin-mot-dau-moi-doi-voi-cac-to-chuc-niem-yet-va-dang-ky-giao-dich-tren-hose-61853.html>
- Late filings and extensions:
  <https://daidoanket.vn/vinh-hoan-bi-hose-nhac-nho-vi-cham-nop-bao-cao-tai-chinh-quy-i-2024-10278996.html>,
  <https://baovanhoa.vn/kinh-te/dau-tu-thuong-mai-smc-bi-nhac-nho-cham-cong-bo-bctc-kiem-toan-nam-2024-127511.html>
- HOSE information service: <https://www.hsx.vn/vi/dich-vu/cung-cap-thong-tin>.
  Disclosure listing: <https://www.hsx.vn/vi/tin-tuc/tin-to-chuc-niem-yet>
- Vietcap terms of use: <https://www.vietcap.com.vn/dieu-khoan-su-dung-chinh-sach-bao-mat>
- Circular 99/2025/TT-BTC in force from 1 January 2026:
  <https://thuvienphapluat.vn/phap-luat-nha-dat/thoi-han-nop-bao-cao-tai-chinh-tu-112026-theo-thong-tu-992025ttbtc-10792.html>
