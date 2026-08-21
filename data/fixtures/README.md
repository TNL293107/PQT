# Fixtures

Tiny synthetic datasets, tracked by Git so a fresh clone can exercise the
pipelines end to end.

Nothing here came from a provider, a licensed feed, or an exchange, and nothing
that did may be added. The rules in [`../README.md`](../README.md) apply.

| Fixture | Format | Used by |
| ------- | ------ | ------- |
| [`instruments.csv`](instruments.csv) | [instrument symbol list](../schemas/instrument-csv.md) | the file instrument source, for provider import |
| [`market-data/`](market-data/) | [market data CSV](../schemas/market-data-csv.md) | the file market data source, for bar ingestion |

## Instrument symbol list

Real Vietnamese tickers and registered company names — public reference data,
carrying no price, no volume and no financial figure. The `isin` and `figi`
columns are deliberately empty: those are real identifiers that would have to
be sourced, and an invented one that happened to pass its check digit would be
worse than none.

Point the file instrument source at it and run an import:

```
MarketData__InstrumentListPath=data/fixtures/instruments.csv
```

The venues must exist first — the import rejects a row whose exchange the
system does not hold, rather than creating one. Reference data seeding creates
HOSE, HNX and UPCOM.

## Market data

See [`market-data/README.md`](market-data/README.md). Its ticker is `DEMO`,
which is not listed on any Vietnamese venue and is not in the symbol list
above; register an instrument under it before ingesting.
