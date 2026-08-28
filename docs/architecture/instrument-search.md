# Instrument search, resolution and current security

How the terminal turns what a user types into a canonical security, and how
every later module reads which security that is.

The decisions behind this design, and what was rejected, are recorded in
[ADR-010](decisions/ADR-010-instrument-search-and-security-context.md). Identity
itself is [ADR-009](decisions/ADR-009-instrument-identity-and-ticker-lifecycle.md).

## Identity, in one paragraph

An instrument's identity is an internal `InstrumentId` (a version 7 UUID),
issued by this system. Ticker, name and exchange are mutable attributes.
Vietnamese tickers change on exchange transfer and are reassigned after a
delisting, so anything that keys off a ticker eventually points at a different
company. **The UI displays the ticker. Everything else joins on the
identifier.**

## Search architecture

```
   query text
        │
        ▼
InstrumentSearchText.Normalise      strip diacritics, upper-case, collapse spaces
        │
        ▼
InstrumentSearchCriteria.TryCreate  validate, bound the limit           (Application)
        │
        ▼
IInstrumentSearchService            observability seam                  (Application)
        │
        ▼
IInstrumentRepository.SearchAsync   one bounded, ranked SQL query    (Infrastructure)
        │
        ▼
IReadOnlyList<InstrumentSearchResult>
```

Both sides of every comparison pass through the same folding function, so
matching is ordinal and behaves identically in the CLR and in PostgreSQL. The
folded values are persisted on the aggregate:

| Column          | Contents                                       |
| --------------- | ---------------------------------------------- |
| `search_ticker` | The ticker, folded                             |
| `search_name`   | The name, folded — `Công ty` → `CONG TY`        |

Both carry a btree index built with `varchar_pattern_ops`, without which
PostgreSQL will not use an index for `LIKE 'ABC%'`.

`search_ticker` deliberately duplicates the ticker's characters. `Instrument.Ticker`
is persisted through a value converter, which EF Core can compare for equality
but cannot pattern-match — and ticker prefix search is the most-used query in
the terminal. The aggregate maintains both columns on every path that changes a
ticker or a name.

## Ranking

Deterministic, evaluated in the database, and applied before the limit.

| Rank | Match          | Example — query `FPT`                          |
| ---- | -------------- | ---------------------------------------------- |
| 1    | `ExactTicker`  | **FPT** — FPT Corporation                      |
| 2    | `TickerPrefix` | FPTS                                           |
| 3    | `ExactName`    | a security named exactly `FPT`                 |
| 4    | `NamePrefix`   | FOX — **FPT** Telecom Joint Stock Company      |
| 5    | `NameContains` | a company mentioning FPT mid-name              |
| 6    | `IdentifierExact` | a query that is exactly an ISIN, FIGI or provider symbol |

Ties break by ticker, then by identifier, so the order is total: two identical
queries against unchanged data return the same rows in the same order. A search
box whose results reshuffle between keystroke and Enter is worse than one that
is merely wrong.

**Identifier matching is exact only, and ranks last.** An alias is an
identifier rather than searchable text: a prefix of an ISIN identifies nothing,
and matching a fragment of one would return every security sharing a country
prefix. Ranking it last sounds wrong for an exact match and is not — nobody
types twelve characters of ISIN into a command bar by accident, so nothing else
is competing with it, and where something is, the query looked like a ticker or
a name and that is what the user meant.

**Wildcards are escaped, not stripped.** `%` and `_` in a query are matched
literally, so a user cannot turn a prefix search into a full scan, and a search
for `100%` finds a company with a percent sign in its name.

## API contract

Read-only. Instruments arrive through the provider import pipeline, not
through HTTP.

### `GET /instruments/search`

| Parameter         | Type   | Default | Notes                            |
| ----------------- | ------ | ------- | -------------------------------- |
| `q`               | string | —       | Required. Max 64 characters.     |
| `limit`           | int    | 20      | 1–50.                            |
| `includeInactive` | bool   | false   | Include delisted instruments.    |

```
200  { "query": "FPT", "count": 1, "limit": 20, "results": [ … ] }
400  problem details — blank query, over-long query, limit out of range
```

A query that matches nothing is `200` with an empty list. "You did not ask me
anything" and "nothing matches what you asked" are different situations, and a
client that cannot tell them apart shows the user the wrong message.

Each result:

```json
{
  "instrumentId": "01a0006b-11a6-7652-9359-62e09c270e56",
  "ticker": "FPT",
  "name": "FPT Corporation",
  "assetType": "Equity",
  "exchange": "HOSE",
  "currency": "VND",
  "status": "Listed",
  "matchKind": "ExactTicker"
}
```

`matchKind` is null outside search results, where nothing was ranked. There is
no price: market data arrives in Phase 2, and a field that is always null would
invite a UI that pretends otherwise.

### `GET /instruments/resolve`

| Parameter  | Type   | Notes                                  |
| ---------- | ------ | -------------------------------------- |
| `symbol`   | string | The ticker to resolve.                 |
| `exchange` | string | Optional venue, to disambiguate.       |

One body shape at every status; the status carries the meaning.

```
200  outcome = Resolved    instrument populated
404  outcome = NotFound    nothing active answers to the symbol
409  outcome = Ambiguous   candidates populated — live on more than one venue
400  problem details       the exchange code is not valid
```

Ambiguity is a result, not an error. Ticker uniqueness is enforced per venue,
so the caller has to be able to choose.

### `GET /instruments/{instrumentId}`

```
200  the instrument
404  problem details
```

The trusted path behind a stored selection: a client sends the identifier it
holds and the server re-reads every attribute, rather than believing the ticker
and name that arrived with the request.

## Security context

```
                      CurrentSecurityProvider          (above the router)
                                │
                    useCurrentSecurity()
                                │
        ┌───────────────┬───────┴───────┬───────────────┐
        ▼               ▼               ▼               ▼
  Security bar      Quote (1.3)     Chart (1.4)      News (1.5)
```

One context, holding the **whole instrument** rather than a ticker string. It
sits above the router because a selection is terminal state, not page state,
and must survive navigation.

Context rather than a store because the value changes when a human picks a
different security — a few times a minute at most. Streaming quotes for that
security are a different problem and do not belong here.

Selecting replaces the value outright, so nothing of the previous security is
readable afterwards.

## Keyboard flow

```
Ctrl+K  (or Cmd+K)   open, input focused
type                 debounced 140 ms, previous request aborted
↑ / ↓                move the highlight, wrapping at both ends
Enter                select — sets the current security, closes the overlay
Esc                  cancel, context untouched
```

No step requires a pointer. The shortcut is registered on the document so it
works wherever focus happens to be.

## How a later module consumes the current security

```tsx
import { useCurrentSecurity } from "../context/currentSecurity";

export function QuotePanel() {
  const { security } = useCurrentSecurity();

  if (security === null) {
    return <EmptyState message="Select a security with Ctrl+K." />;
  }

  // Join on the identifier. Never on security.ticker.
  return <Quote instrumentId={security.instrumentId} />;
}
```

Server-side, the equivalent is `IInstrumentResolver`:

```csharp
var resolution = await resolver.ResolveAsync("FPT", cancellationToken: ct);

if (resolution.Outcome is InstrumentResolutionOutcome.Resolved)
{
    var id = resolution.Instrument!.InstrumentId;
    // …
}
```

Commands, watchlists, alerts, the portfolio and the Phase 18 research analyst
all take this path. None of them should reach for the search service, and none
of them should hold a ticker.

## Command bar

`parseCommand` splits an entry into a security and an optional function
mnemonic, so `FPT GP` searches for `FPT` and reports that `GP` is not available
yet — rather than searching the instrument master for a company called
"FPT GP".

`KNOWN_FUNCTION_CODES` is an allow-list, not a shape rule. A rule such as "a
short trailing word is a function" would split `Hoa Phat` into a security `HOA`
and a function `PHAT`, and short words are exactly what Vietnamese company
names are made of. Later phases add their mnemonics to that list.

Nothing in the parser knows any ticker. Whether the security half names a real
instrument is decided against the instrument master.

## Development data

`Postgres:SeedReferenceDataOnStartup` creates the three venues and a starter
set of Vietnamese securities, so a fresh database has something to find. It is
on in Development and Compose, off everywhere else.

It creates what is missing and never modifies what exists, so a record
corrected by hand survives the next start-up. The securities are recorded as
trading with **no listing date** — the real dates are public but unsourced
here, and the Phase 2 import fills them in from something citable.
