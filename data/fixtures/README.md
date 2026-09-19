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
| 2026 | 13 | 16 – 20 Feb |

**One of those 59 came from being caught rather than transcribed.** The annual
notice for 2026 listed only 1 January as closed; the government later swapped
Friday 2 January to Saturday 10 January and the exchange announced the extra
closed session separately. The first real ingest raised a `MissingSession`
finding for that Friday, and the correction was then taken from the later
announcement — not from the data. A calendar fitted to the observed bars cannot
find a missing bar.

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

Eleven rows, and they are not all of a kind. Two are invented actions against
`DEMO`, the same security the market data fixture describes, so the adjustment
pipeline can be run end to end on a fresh clone. **Nine are real**: the FPT
entitlement of 2016 (below), and the seven VN30 entitlements of August and
September 2026 (under *The VN30 entitlements*).

```
MarketData__CorporateActionPath=data/fixtures/corporate-actions.csv
```

The cash dividend is fitted to the price series on purpose. `DEMO` closes at
25,050 on 2026-08-07 and at 24,700 on 2026-08-10, and the dividend is 350 — so
the whole of the final session's decline is the dividend coming out, and the
adjusted series is flat across it where the raw series drops. That is what an
adjusted chart is for, visible on six sessions.

Both `DEMO` rows are currently **rejected** on import as `UnknownInstrument`:
`DEMO` is not in `instruments.csv`, and the corporate action import matches the
`FILE` source's own symbol alias with no fallback to a bare ticker. Register an
instrument under `DEMO` first, as the market data fixture says.

### The FPT entitlement

```csv
FPT.HM,CashDividend,2016-05-27,,1000,,2016-06-10,
FPT.HM,StockDividend,2016-05-27,0.15,,,,
```

These two are **real**, and they are the Gate A acceptance case: one known
entitlement reproduced through the adjustment engine against raw bars from a
live source. They are what `docs/roadmap/pqt-roadmap-v2.md` U3 and
[ADR-021](../../docs/architecture/decisions/ADR-021-raw-vietnamese-price-history.md)
describe.

On 27 May 2016 two entitlements went ex on `FPT` together: the remaining FY2015
cash dividend of 1,000₫ per share, paid on 10 June 2016, and the FY2015 stock
dividend at 20:3 — three new shares for every twenty held, so `ratio` is the
`0.15` *additional* shares per share the format asks for, not the `20:3` the
announcement is worded in.

The raw close gapped from 47,500 to 41,000, −13.68%, which HOSE's ±7% band makes
impossible as a price move. Their factors multiply to `0.8512585812`, and
`47,500 × 0.8512585812 = 40,434.78` against the 41,000 that printed: +1.40%, an
ordinary session.

**`announced_on` is deliberately empty.** The ex-date, the ratio, the cash
amount and the payment date are all sourced; the announcement date is not, and
inventing one would make a strict as-of read claim knowledge nobody has
evidenced. It is the case `AnnouncementPolicy` exists for — the actions apply
under `permissive` and are withheld under `strict`. `record_date` is empty for
the same reason.

The symbol is `FPT.HM`, not `FPT`, because the corporate action import matches
the `FILE` source's alias exactly — the spelling `instruments.csv` carries.

The second row is a share issuance, which is recorded and rescales nothing. It
is there so the fixture exercises the path where an action is a real fact about
the issuer and still produces no factor — a case that reads identically to a
failed computation unless something distinguishes them.

The import resolves symbols through the provider alias the **instrument**
import wrote, so an instrument must be registered under `DEMO` for the same
source first; otherwise both rows are refused as `UnknownInstrument`. There is
no fallback to the bare ticker, deliberately.

### The VN30 entitlements

The first VN30 backfill (raw CafeF bars, July to September 2026) raised five
price-limit breaches. Every one was an entitlement going ex, and each is
transcribed from its VSD notice ("Thông báo ngày đăng ký cuối cùng", vsdc.vn),
which gives the terms and the record date. The ex-date is the session before the
record date, and the press states it outright in each case.

| Session | Security | Actions | Raw move | Reference after the actions | Move from it |
| ------- | -------- | ------- | -------- | --------------------------- | ------------ |
| 2026-08-06 | VHM | 1:1 stock dividend | −49.61% | 76,500 | +0.78% |
| 2026-08-11 | MBB | 100:15 stock dividend; 10:1 rights at 10,000₫ | −16.08% | 20,200 | +0.75% |
| 2026-08-17 | SSI | 1,000₫ cash dividend; 5:1 bonus shares | −19.18% | 19,583 | +1.11% |
| 2026-09-10 | VIB | 100:9.5 bonus shares | −8.67% | 13,699 | +0.01% |
| 2026-09-17 | TCX | 5:1 stock dividend | −16.64% | 31,792 | +0.03% |

The references were computed by hand from the published terms and then matched
by the engine to the đồng. The terms were not fitted to the prices.

**MBB found an engine bug.** Its dividend and its rights both count the
shares held at the record date, so a holder of 100 ends with 125 shares worth
`100P + 10S`, a reference of 20,200. Adjustment rules version 1 multiplied the
two standalone factors, which divides by `1.15 × 1.1` and lands on 19,960. That
is 1.2% off and inside the band, so no check would ever have caught it. Version
2 composes each session's actions together; see
`AdjustmentFactors.TryComposeSession`.

**`announced_on` is the VSD notice's date.** For every row the issuer disclosed
earlier, but its exact date could not be sourced for most of them. The VSD date
is the latest candidate, so a strict as-of read claims no knowledge before the
evidence. That makes it conservative, and it is not the true first publication.

Deliberately not recorded:

- **Share delivery dates.** None of the notices gives one.
- **VHM's 6,000₫ cash dividend.** It paid on 22 July and went ex on another
  session, outside this history.
- **VIB's ESOP issue.** It is not pro rata and rescales nothing.

## Universes

Three sets in [`universes/`](universes/). `DEMO_INDEX` and `DEMO_EMPTY` are
invented and make no claim about a real index. `VN30` is real, sourced, and
**covers 4 August 2025 up to 18 September 2026 and nothing else** — read
outside that span it answers *unknown*, which is the point. Seeding today's
constituents and letting them stand in for earlier years would be the
survivorship bias this workstream exists to remove.

```
MarketData__UniverseDirectory=data/fixtures/universes
```

### VN30

Transcribed from HOSE's own constituent tables ("Công bố thông tin danh mục cổ
phiếu thành phần chỉ số VN30") for three reviews, plus one change between
reviews. Like the trading calendar, this is an exchange's public announcement,
not vendor data. HOSE's site served an empty page, so the tables were read from
copies of HOSE's documents hosted by Vietstock.

| Effective | Change | Announced | Basket source |
| --------- | ------ | --------- | ------------- |
| 2025-08-04 | BVH out, DGC in; coverage starts here | 2025-07-17 | Kỳ 7/2025 table (HOSE) |
| 2026-02-02 | BCM out, VPL in | 2026-01-21 | Kỳ 1/2026 list (HOSE PDF) |
| 2026-05-13 | DGC out, BSR in — extraordinary: DGC moved to the controlled list | 2026-05-07 | secondary (press); consistent with BSR heading the January reserve list |
| 2026-08-03 | PLX, TPB out; MCH, TCX in | 2026-07-15 | Kỳ 7/2026 table (HOSE) |

The quarterly reviews of October 2025 and April 2026 changed no members (the
October one is primary; the April one is secondary). The four resulting
baskets were checked by a script against the three published tables: 30 names
on every date, and each basket equals the previous one plus the additions and
minus the removals.

Where the record is weaker than the membership itself:

- **Announcement dates.** 2025-07-17 is the earliest date the announcement is
  evidenced as published (one secondary source says the 16th — the later date is
  used so a strict as-of read claims no knowledge earlier than the evidence).
  2026-01-21 is inferred from the HOSE PDF's file name and upload path.
- **Effective dates** are from press reports of HOSE's announcement, not from
  HOSE's own wording.
- **Coverage ends on 2026-09-18, exclusive.** The search for changes after
  3 August 2026 was brief. Extending the span means checking for any
  extraordinary replacement before moving `coverage_until`, not only
  transcribing the next review.
- **Names** in the symbol list for the VN30 additions are the Vietnamese
  registered names exactly as HOSE printed them; an English name would have been
  a translation nobody sourced. STB is printed "Sài Gòn Thương Tín" in 2025 and
  "Sài Gòn Tài Lộc" in the July 2026 table; the long-standing name is kept, and
  the import never overwrites a name anyway.

**BSR moved from UPCOM to HOSE.** The symbol list and the development seed had
it on UPCOM, which made CafeF — which does not serve UPCOM — refuse it and
judged its prices against the wrong band. Both now say HOSE. A database seeded
before the move is corrected with `pqt instrument transfer --instrument BSR --to
HOSE`; the import will not infer a transfer from a ticker.

### The demonstration sets

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
