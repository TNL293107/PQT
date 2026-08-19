# Market data fixture

A synthetic six-session daily series for a security that does not exist, in
the format described by [`../../schemas/market-data-csv.md`](../../schemas/market-data-csv.md).

Every number here was made up. Nothing in this directory came from a provider,
a licensed feed, or an exchange, and nothing that did may be added to it — the
rules in [`../../README.md`](../../README.md) apply.

It exists so the ingestion pipeline can be run end to end on a fresh clone:

```
MarketData__FileProviderDirectory=data/fixtures/market-data
```

The ticker is `DEMO`, which is not listed on any Vietnamese venue. To ingest
it, register an instrument under that ticker first; the pipeline stores bars
against the instrument's canonical identifier, and a file with no matching
instrument is a run recorded as skipped.
