# Instrument symbol list CSV

The interchange format the file-backed instrument source reads. It is a real
provider from the import pipeline's point of view: the same normalisation, the
same deduplication, the same rejections as a vendor feed, and no licence needed
to run it.

Configure it by pointing `MarketData__InstrumentListPath` at the file. With
that setting blank, no instrument source is registered and an import refuses
with a stated reason rather than silently doing nothing.

## Columns

A header row is required. Columns are matched **by name**, not by position.

| Column      | Required | Meaning                                            |
| ----------- | -------- | -------------------------------------------------- |
| `symbol`    | yes      | The provider's spelling, decoration included        |
| `name`      | yes      | The security name                                   |
| `exchange`  | no       | The venue's operating code                          |
| `asset_type`| no       | `Equity`, `Etf`, `Index`, `Futures`, …              |
| `currency`  | no       | ISO 4217 code; defaults to `VND`                    |
| `isin`      | no       | ISO 6166 identifier                                 |
| `figi`      | no       | OpenFIGI identifier                                 |
| `listed_on` | no       | First trading date, `yyyy-MM-dd`                    |

```csv
symbol,name,exchange,asset_type,currency,isin,figi,listed_on
FPT.HM,FPT Corporation,HOSE,Equity,VND,,,2006-12-13
SHS.HN,Saigon - Hanoi Securities Joint Stock Company,HNX,Equity,VND,,,
```

Almost everything is optional because that is what symbol lists actually look
like. A vendor publishing tickers and names but no ISIN, no listing date and no
asset class is the common case, and refusing those rows would mean importing
nothing.

## How a row becomes an instrument

```
symbol → normalise → deduplicate → match or create → record the alias
```

**The symbol is split into a ticker and a venue hint.** `FPT`, `FPT.HM`,
`FPT:VN`, `HOSE:FPT` and `FPT-HNX` all resolve to the ticker `FPT`. The
separators `. : - / _` and a space are all recognised, and the decoration is
kept: the provider's exact spelling is stored as an alias so the next import is
a lookup rather than a re-parse.

**A stated `exchange` beats the symbol's decoration.** A vendor's suffix can
lag an exchange transfer by months, and the row's own field is the more
considered answer.

**Deduplication is tried strongest first:**

1. this provider's own symbol, from a previous import
2. a global identifier — ISIN, then FIGI
3. the ticker on its venue

The order is the point. A ticker match is the weakest of the three, because
tickers are reused after delisting and change on an exchange transfer, so it is
consulted last rather than first.

## What is rejected, and why

| Condition                                    | Reason                    |
| -------------------------------------------- | ------------------------- |
| The symbol splits into two possible tickers   | `UnreadableSymbol`        |
| No name                                       | `UnusableName`            |
| No venue stated and none in the symbol        | `UnknownExchange`         |
| A venue the system does not hold              | `UnknownExchange`         |
| An ISIN or FIGI failing its check digit       | `InvalidIdentifier`       |
| Identifiers and symbol pointing at different instruments | `ConflictingIdentity` |
| The same symbol twice in one file             | `DuplicateWithinImport`   |

A rejected row does not stop the import. A symbol list is thousands of rows and
some of them are always wrong; throwing would mean importing nothing.

`ConflictingIdentity` is the one that matters most. Resolving it either way
would merge two securities or split one, so it is left for a human.

## What an import will not do

**It never deletes and never delists.** A security absent from a provider's
list has not necessarily stopped trading — the vendor may have dropped
coverage, or the file may be truncated — and inferring a lifecycle transition
from an absence is how a live security silently disappears.

**It never overwrites what the master already holds.** The name, asset class
and listing date of an existing instrument are left alone: a provider's
spelling of a company name differs from the registered one, and a listing date
it invented would overwrite a sourced one. Enrichment is additive and limited
to aliases.

## Licensing

Do not commit vendor symbol lists to this repository. `data/raw/`,
`data/cache/` and `data/external/` are ignored by Git precisely so that a
licensed dataset cannot be added by accident. A synthetic fixture lives in
[`../fixtures/instruments.csv`](../fixtures/instruments.csv).
