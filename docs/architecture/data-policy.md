# Financial Data Policy

Market data is licensed, not owned. This repository's proprietary licence
covers its source code and says nothing about the data the software may one
day retrieve — that is governed entirely by whichever provider supplied it.

**Status:** no *licensed* provider is integrated and no API key is in use. The
only prices in this repository are a six-session synthetic series under
`data/fixtures/`, invented for a ticker that is not listed on any venue, so the
ingestion pipeline can be run on a fresh clone. No vendor data is present, and
the rules below are what keep it that way.

## Rules

1. **No vendor data in Git.** Not prices, not fundamentals, not filings, not
   article text. Bulk data lives in the ignored directories described in
   [`../../data/README.md`](../../data/README.md).
2. **No provider credentials in Git.** Keys belong in `.env`, which is ignored.
   `.env.example` carries empty placeholders only.
3. **Fixtures are synthetic or trivially small.** A fixture exists to make a
   test deterministic. If a fixture would be recognisable as a real vendor
   extract, it is too large and probably not licensed for the purpose.
4. **Attribution where required.** Several providers require visible
   attribution in any interface displaying their data. That obligation is
   recorded per provider when one is adopted.
5. **Personal use is not commercial use.** Most retail-priced feeds permit
   personal research and prohibit redistribution, resale, or use in a
   commercial product. This project is personal research. If that ever
   changes, every provider agreement must be re-read first.

## Source tiers

Data reaches PQT at three tiers. The distinction is **ownership and
obligation**, not quality.

| Tier | What | Examples | Status |
| --- | --- | --- | --- |
| **Tier 1 — PQT Native** | Vietnamese market data PQT ingests directly and treats as its subject | HOSE, HNX, UPCOM, VN30; Vietnamese fundamentals, corporate actions, ownership, news | The project. U3 delivers the first real one |
| **Tier 2 — External** | Non-Vietnamese or supplementary data reached through an optional, out-of-process collector | US and global equities, ETFs, options, futures, FX, crypto, global macro and news | `DEFERRED`. OpenBB is one possible route — see [ADR-019](decisions/ADR-019-openbb-boundary.md) |
| **Tier 3 — User / private** | Data the operator supplies | CSV exports, broker statements, proprietary factors, private research sets | The file-ingestion seam already serves this |

### Access broadly, canonicalize selectively

> Being able to *reach* a dataset costs nothing. Making it **canonical** costs a
> domain model, a quality rule set, a provenance story and a maintenance
> obligation forever.

**No Tier 2 or Tier 3 data becomes canonical without passing the same stages
Tier 1 data passes** — normalisation, validation, provenance, point-in-time,
adjustment. There is no shortcut into `quant.*` and there is no second pipeline.

This is what lets PQT contemplate a broad external ecosystem without inheriting
hundreds of bespoke domain models: most external data is *consulted*, and only
the little that earns a place is *canonicalized*. It is also why breadth is not
a goal. A dataset PQT cannot state the provenance of is not an asset.

## Provider evaluation checklist

No provider is integrated until each of these is answered in writing, in the
ADR that adopts it.

### Licensing

- [ ] **What licence covers the connector, and what licence covers the data?**
      These are two separate questions and both must be answered. An
      open-source client says nothing about whether the data it retrieves may
      be stored or redistributed, and a permissively licensed feed may still
      arrive through a copyleft library whose terms bind the software.
- [ ] What licence covers the data — not the API, the data?
- [ ] Is personal, non-commercial research explicitly permitted?
- [ ] Is **redistribution** permitted, in any form?
- [ ] Is **storage** permitted, and for how long? Some feeds permit display but
      not retention, which rules out backtesting outright.
- [ ] Is derived data (factors, signals, aggregates) treated differently from
      raw data?
- [ ] Is attribution required, and where must it appear?
- [ ] What happens to stored data if the subscription ends?

### Technical

- [ ] Rate limits, and what happens when they are hit.
- [ ] Coverage: which venues, which asset classes, how far back?
- [ ] Are corporate actions and splits adjusted, and is the adjustment
      reversible?
- [ ] **Are restatements and corrections published?** A feed that silently
      rewrites history makes reproducible backtests impossible.
- [ ] Does fundamental data carry a publication timestamp as well as a fiscal
      period? Without it, look-ahead bias cannot be avoided.
- [ ] Symbol identifiers: does the provider expose ISIN or FIGI, or only its
      own symbology?
- [ ] Does the provider preserve history across exchange transfers and symbol
      changes, or does it silently start a new series?
- [ ] Are rights issues and bonus shares reported as distinct action types, or
      collapsed into a generic dividend?
- [ ] Does it cover HOSE, HNX and UPCOM, or only the main board?

### Operational

- [ ] Cost, and how it scales with usage.
- [ ] Availability and status reporting.
- [ ] Terms-of-service change history — how often, with how much notice?

## Identifiers

| Identifier      | Position for Vietnamese equities                                  |
| --------------- | ------------------------------------------------------------------ |
| Ticker          | Not stable and not unique over time. Never a primary key.          |
| Exchange code   | Meaningful only together with the exchange and a date range.       |
| ISIN            | Assigned to Vietnamese securities but rarely exposed by providers. |
| FIGI            | Coverage exists but is inconsistent; not dependable as the key.    |
| CUSIP           | Not applicable.                                                    |

Phase 1 — Instrument Master therefore issues an **internal canonical ID** as
the primary key, with provider and market identifiers stored as aliases.

In a US-centric system that choice is good practice. Here it is the only
workable option: tickers change on exchange transfer (UPCOM → HNX → HOSE is a
normal progression), and a delisted ticker can be reassigned to an unrelated
company. A ticker used as a key would eventually merge two different
securities into one row.

Where a licensed identifier such as ISIN is available it is stored as an alias
only, so no licensed identifier ever becomes structurally load-bearing.

## Retention

When data arrives, retention is set per dataset and recorded:

- Raw provider payloads: kept only as long as the licence permits.
- Normalised data: the working dataset.
- Derived data: factors and signals, rebuildable from the above.

## If a licence is violated

1. Stop the ingestion path.
2. Delete the affected data from all storage, including backups and caches.
3. Verify nothing reached Git history.
4. Record what happened and why in an ADR, so the same evaluation gap is not
   repeated.
