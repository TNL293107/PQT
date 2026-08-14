# ADR-010: Instrument search, resolution and the current-security context

**Status:** Accepted · **Date:** 2026-08-14 · **Phase:** 1 (workstreams 2, 6, 7)

## Context

[ADR-009](ADR-009-instrument-identity-and-ticker-lifecycle.md) established that
an instrument's identity is an internal `InstrumentId` and that the ticker is a
mutable attribute. That decision is only worth anything if the rest of the
system can *find* an instrument and then *refer to it by that identity*.

Three things were missing:

- **Discovery.** A trader types `FPT`, or `vinhomes`, or `ngan hang`, and has
  to arrive at one security.
- **Resolution.** Commands, watchlists, alerts and the eventual research agent
  need to turn a symbol into a canonical instrument without a search box in the
  loop.
- **A subject.** Every panel from Phase 2 onwards describes *a* security. If
  each one holds its own idea of which, the terminal shows a screen of numbers
  whose subject is ambiguous.

Two constraints shaped the answer. Matching has to be case- and
accent-insensitive, because Vietnamese company names carry diacritics that
nobody types. And it has to be cheap, because search runs on every keystroke of
the command bar.

## Decision

**Matching is ordinal, over folded columns.** `Instrument` maintains two search
columns — `search_ticker` and `search_name` — folded by
`InstrumentSearchText.Normalise`: diacritics stripped, upper-cased invariantly,
internal whitespace collapsed. Queries pass through the same function, so
matching is plain ordinal comparison.

**Ranking is a closed, ordered enumeration** (`InstrumentMatchKind`), evaluated
in SQL:

```
1  ExactTicker    query is the ticker
2  TickerPrefix   ticker begins with query
3  ExactName      query is the name
4  NamePrefix     name begins with query
5  NameContains   name contains query
```

Ordering is total — rank, then ticker, then identifier — and the limit is
bounded (default 20, maximum 50) and applied in the database.

**Resolution is a separate service from search.** `IInstrumentResolver` answers
by ticker only, and reports one of three outcomes: `Resolved`, `NotFound`, or
`Ambiguous` with its candidates.

**The current security is one context, holding the whole instrument.** Not a
ticker string, and not state inside the search component.

## Alternatives

**A case-insensitive or accent-insensitive collation.** Rejected: the answer
would depend on the server's locale, the same query would rank differently on
two machines, and a prefix search could no longer use a plain btree index.

**`pg_trgm` with a GIN index, or an external search engine.** Rejected as
premature. The Vietnamese instrument master is a few thousand rows; the phase
that justifies either is the one where it is not.

**Matching the ticker through its value converter.** Not possible: EF Core can
compare a value-converted property for equality but cannot pattern-match it, so
ticker prefix search would have to filter in application memory. Hence the
duplicated `search_ticker` column.

**Ranking in application code after a broad query.** Rejected: it is the
`SELECT *`-then-filter shape, and it stops working at exactly the table size
the instrument master is meant to reach.

**Resolving a company name as well as a ticker.** Rejected: resolution answers
a question with one correct answer, and a name would make the result depend on
what else happens to be listed. Free text belongs in search.

**Storing only the ticker as the current security.** Rejected for the reason
ADR-009 exists — a ticker changes on exchange transfer and is reassigned after
delisting, so a module holding one eventually describes a different company.

## Reasoning

Folding both sides of the comparison moves the hard part — Vietnamese
diacritics, casing, stray whitespace — into one pure, tested function, and
leaves the database doing something it is fast at.

Ranking in SQL keeps the whole query one bounded round trip, and expressing the
tiers as an enumeration makes the priority the contract rather than a side
effect of how the query happens to be written.

Ambiguity is a first-class outcome because it is normal: ticker uniqueness is
enforced *per venue*, so the same three letters can be live on HOSE and UPCOM
at once. Collapsing that to "not found", or silently taking the first row,
would eventually attach one company's prices to another's identifier.

## Trade-offs

- **`search_ticker` duplicates the ticker's characters.** Accepted, because the
  alternative is either an untyped canonical column or prefix filtering in
  application memory. The aggregate maintains it on every path that changes the
  ticker, and a unit test asserts the two never disagree.
- **`NameContains` cannot use an index.** An infix `LIKE '%x%'` is a scan.
  Bounded by the result limit and by the table's size; `pg_trgm` is the answer
  when that stops being true.
- **Diacritic folding is one-way.** `CONG` matches both `Cộng` and `Công`. For
  discovery that is the desired behaviour, not a defect.
- **No identifier search.** ISIN, FIGI and provider symbols rank after
  `NameContains` when the alias workstream lands. They are absent rather than
  stubbed because no alias data exists to match against.
- **Seeded securities carry no listing date.** Their real dates are public but
  unsourced here, and an unsourced date in the system of record is the failure
  mode the instrument master exists to prevent.

## Consequences

- Both search columns are maintained by the aggregate. A future write path that
  changes a ticker or a name without going through `Instrument` makes the
  security unfindable, and nothing above the database can detect that.
- Search results are a projection (`InstrumentSearchResult`), never the
  aggregate, and never a persistence entity on the wire.
- The API is read-only. Instruments arrive through the provider import
  pipeline, not through HTTP.
- Phase 1.3 onwards consumes `useCurrentSecurity()` and reads
  `security.instrumentId`. No module keys off the ticker.
- A client-supplied identifier is re-read server-side through
  `GET /instruments/{id}`; nothing trusts the attributes that came with it.
