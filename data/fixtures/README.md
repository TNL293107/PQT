# Fixtures

Tiny synthetic datasets, tracked by Git so a fresh clone can exercise the
pipelines end to end.

Nothing here came from a provider, a licensed feed, or an exchange, and nothing
that did may be added. The rules in [`../README.md`](../README.md) apply.

| Fixture | Format | Used by |
| ------- | ------ | ------- |
| [`instruments.csv`](instruments.csv) | [instrument symbol list](../schemas/instrument-csv.md) | the file instrument source, for provider import |
| [`trading-calendar.csv`](trading-calendar.csv) | [trading calendar](../schemas/trading-calendar-csv.md) | the file calendar source, for completeness scoring |
| [`market-data/`](market-data/) | [market data CSV](../schemas/market-data-csv.md) | the file market data source, for bar ingestion |
| [`corporate-actions.csv`](corporate-actions.csv) | [corporate action CSV](../schemas/corporate-action-csv.md) | the file corporate action source, for adjusted prices |
| [`universes/`](universes/) | [universe CSV](../schemas/universe-csv.md) | the file universe source, for point-in-time constituent sets |

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

## Trading calendar

**Incomplete on purpose, and not usable as a real calendar.** It carries only
the four statutory fixed-date Vietnamese holidays, which are the ones that can
be stated without a source. Tet and the Hung Kings commemoration follow the
lunar calendar, and substitute days for holidays falling at a weekend are set
by annual decree — none of those can be derived, and inventing them here would
put dates into the system of record that nothing stands behind.

The consequence is visible rather than hidden: import this file and the system
will believe its calendar covers 2026, then report the whole of Tet as missing
sessions. It exists to exercise the import and the completeness rules, not to
be trusted. Replace it with a real calendar before reading a completeness
figure.

## Market data

See [`market-data/README.md`](market-data/README.md). Its ticker is `DEMO`,
which is not listed on any Vietnamese venue and is not in the symbol list
above; register an instrument under it before ingesting.

## Corporate actions

Two invented actions against `DEMO`, the same security the market data fixture
describes, so the adjustment pipeline can be run end to end on a fresh clone:

```
MarketData__CorporateActionPath=data/fixtures/corporate-actions.csv
```

The cash dividend is fitted to the price series on purpose. `DEMO` closes at
25,050 on 2026-08-07 and at 24,700 on 2026-08-10, and the dividend is 350 — so
the whole of the final session's decline is the dividend coming out, and the
adjusted series is flat across it where the raw series drops. That is what an
adjusted chart is for, visible on six sessions.

The second row is a share issuance, which is recorded and rescales nothing. It
is there so the fixture exercises the path where an action is a real fact about
the issuer and still produces no factor — a case that reads identically to a
failed computation unless something distinguishes them.

The import resolves symbols through the provider alias the **instrument**
import wrote, so an instrument must be registered under `DEMO` for the same
source first; otherwise both rows are refused as `UnknownInstrument`. There is
no fallback to the bare ticker, deliberately.

## Universes

Two invented sets in [`universes/`](universes/), over the real tickers in the
symbol list above. **Neither is a claim about a real index.** `DEMO_INDEX` is
synthetic, and no fixture here states VN30's membership: that history has to be
sourced from published review notices, and inventing it — or seeding today's
constituents and letting them stand in for every earlier year — is the
survivorship bias this workstream exists to remove, committed to the repository.

```
MarketData__UniverseDirectory=data/fixtures/universes
```

`DEMO_INDEX` claims coverage from 2026-01-02 onwards and carries five spells,
including a re-entry: `VNM.HM` leaves on 2026-04-01 and returns on 2026-07-01,
so a constituent read for May finds four names and one for July finds five. The
months between the two spells are the part a survivorship-free backtest has to
be able to see.

`DEMO_EMPTY` is defined and deliberately has no membership and no coverage
claim. Every import raises a coverage finding against it, and every constituent
read of it answers *unknown* rather than returning an empty set. That is the
whole point of the fixture: an unsourced universe and a complete one must never
look alike, and here they demonstrably do not.

Symbols resolve through the provider alias the **instrument** import wrote, so
run that import first; otherwise every row is refused as `UnknownInstrument`.
