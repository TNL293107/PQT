# Financial Data Policy

Market data is licensed, not owned. This repository's proprietary licence
covers its source code and says nothing about the data the software may one
day retrieve — that is governed entirely by whichever provider supplied it.

**Phase 0 status:** no provider is integrated, no API key is in use, and no
market data exists anywhere in this repository.

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

## Provider evaluation checklist

No provider is integrated until each of these is answered in writing, in the
ADR that adopts it.

### Licensing

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
- [ ] Symbol identifiers: does the provider expose FIGI, ISIN, or CUSIP, or
      only its own symbology?

### Operational

- [ ] Cost, and how it scales with usage.
- [ ] Availability and status reporting.
- [ ] Terms-of-service change history — how often, with how much notice?

## Identifier licensing

Instrument identifiers are themselves licensed, and this catches people out:

| Identifier | Position                                                          |
| ---------- | ----------------------------------------------------------------- |
| FIGI       | Openly licensed. Preferred as the canonical external identifier.   |
| ISIN       | National numbering agencies assert rights over bulk redistribution. |
| CUSIP      | Commercially licensed. Redistribution requires an agreement.       |
| Ticker     | Not unique and not stable — never usable as a primary key.         |

Phase 1 — Instrument Master therefore issues an **internal canonical ID** as
the primary key, with provider and market identifiers stored as aliases. That
is the correct data model regardless of licensing, and it also means no
licensed identifier ever becomes structurally load-bearing.

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
