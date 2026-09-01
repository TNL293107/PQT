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
| [`instruments-fpt.csv`](instruments-fpt.csv) | [instrument symbol list](../schemas/instrument-csv.md) | the single-ticker list U3's first real ingest runs against |

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

**Real, sourced, and complete for 2022 through 2026.** 58 closed sessions
across the three venues, every one of them transcribed from the Ho Chi Minh
Stock Exchange's own annual holiday notice.

This is the one fixture that is not synthetic, and it is not vendor data
either: an exchange's published closure schedule is a public announcement, and
the dates in it are facts about which days a market did not open. It is the
only way this system can tell a session that is missing from a session that
never existed.

| Year | Closed sessions | Tet |
| ---- | --------------- | --- |
| 2022 | 11 | 31 Jan – 4 Feb |
| 2023 | 11 | 20 – 26 Jan |
| 2024 | 12 | 8 – 14 Feb |
| 2025 | 12 | 27 – 31 Jan |
| 2026 | 12 | 16 – 20 Feb |

Every Tet closes five trading days. The spans an announcement gives include the
weekends inside them; those are dropped here, because weekends are structural
for every venue this system covers and the format records closures rather than
non-trading days.

Two things the notices state that this file deliberately does not carry:

- **Make-up working Saturdays.** When a holiday is extended by swapping a
  Monday for a Saturday — 4 May 2024, 22 August 2026 — the exchange does not
  trade on the Saturday either. Recording it would be recording a weekend, and
  the calendar already knows about weekends.
- **Settlement-only closures.** A day on which the depository does not settle
  but the exchange does trade is not a closed session, and this file is about
  sessions.

**Coverage ends after 2026.** The 2027 notice is published in late 2026, and
until it is transcribed here the calendar covers what it covers — the
completeness report says `calendarIsComplete` for the range it was asked
about, and a range reaching past the last recorded year is reported as
unmeasured rather than assumed open.

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
