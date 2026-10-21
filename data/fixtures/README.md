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
| [`corporate-action-coverage.csv`](corporate-action-coverage.csv) | `ticker,complete_from,swept_on,source` | which issuers' actions were swept in full, for dividend yield ([ADR-032](../../docs/architecture/decisions/ADR-032-ratios-version-2.md)) |
| [`universes/`](universes/) | [universe CSV](../schemas/universe-csv.md) | the file universe source, for point-in-time constituent sets |
| [`venue-history.csv`](venue-history.csv) | `symbol,exchange,effective_from,effective_to` | the file venue history source, for judging each session on its own venue |
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

**Real, sourced, and complete for 2016 through 2026.** Every closed session is
transcribed from the exchanges' own holiday notices: HOSE's annual notice for
2022–2026, and for 2016–2021 both HOSE's and HNX's annual notices (HNX runs
UPCOM), with HNX's per-holiday notices agreeing. The 2016–2021 sources, notice
by notice, are in
[`trading-calendar-sources-2016-2021.md`](trading-calendar-sources-2016-2021.md).
The coverage declaration runs from 1 January 2016 for all three venues.

This is the one fixture that is not synthetic, and it is not vendor data
either: an exchange's published closure schedule is a public announcement, and
the dates in it are facts about which days a market did not open. It is the
only way this system can tell a session that is missing from a session that
never existed.

| Year | Closed sessions | Tet |
| ---- | --------------- | --- |
| 2016 | 10 | 8 – 12 Feb |
| 2017 | 10 | 26 Jan – 1 Feb |
| 2018 | 11 (HOSE 13) | 14 – 20 Feb |
| 2019 | 11 | 4 – 8 Feb |
| 2020 | 10 | 23 – 29 Jan |
| 2021 | 11 | 10 – 16 Feb |
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

**Two HOSE closures are not holidays.** HOSE's trading system failed in the
closing auction of 22 January 2018, and the exchange did not trade at all on
23 or 24 January; it resumed on the 25th (HOSE's press releases and incident
notice, and HSC's client note, linked in the sources file). HNX and UPCOM
traded normally. Both days are recorded for HOSE only, as `Trading system
outage`, because the file records sessions that did not happen, and without
them every HOSE series would report two missing sessions that never existed.
Partial halts — the cancelled closing auction of 9 June 2020, the halted
afternoon of 1 June 2021 — still had a session and are not recorded.

**The 2016–2021 rows were cross-checked, not derived.** VN-Index, HNX-Index and
UPCoM-Index daily histories from two vendors show no session on any recorded
closure, and the only weekdays without a session the notices do not list are
the two outage days above; every other gap in an index series is a hole in
that one vendor's data, with the other vendor or individual stock bars showing
trading that day.

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

### The 2023–2024 history entitlements (sprint 5)

The sprint-5 backfill took every VN30 name back to 3 July 2023, up to the day
before its first VN30 admission. It raised 31 price-limit breaches, and every
one is now accounted for:

- **23 by entitlements** (25 rows, plus TCX's 500₫ cash dividend of 7 April
  2026, which was too small to breach). ACB, BID, CTG, FPT (×2), GAS, HDB,
  MBB, SAB, SHB, TCB, VCB, VPB and LPB (2024-12-27) are each transcribed from
  their VSD notice. LPB 2023-08-22, MCH 2025-02-11 and 2026-01-09, BSR
  2025-10-29 and TCX 2026-05-25 are transcribed from VSD notices or, for LPB,
  from the issuer's terms as the press reported them, because LPB's VSD notice
  was not found. Each restated move sits inside its venue's band. Notice by
  notice, the sources are in [`sources/`](sources/).
- **10 by venue history.** MCH's UPCOM sessions moved inside UPCOM's ±15% band
  and breached only HOSE's. See [Venue history](#venue-history).
- **2 by CafeF errors**, dismissed with the reason. BSR on 2026-02-02 moved
  across the 2026-01-29/30 gap. DGC on 2024-03-07: CafeF's 2024-03-06 close
  of 111,000 is wrong. VNDirect has 113,000, and a 113,000 reference for the
  7th, so that session was a +6.99% limit-up, and VSD lists no DGC entitlement
  then.

Two transcription choices to know about:

- **MCH 2026-01-09** distributed 21.47% bonus shares and 1.03% treasury
  shares on one record date. Both increase the holding, so they are one
  `BonusShares` row at 0.225. The two cannot be separate rows, because the key
  is (symbol, type, ex-date). HOSE's reference ignored the treasury part. The
  adjusted series follows the holding, not the reference.
- **Issues from owners' equity are `BonusShares`** (GAS, SAB, TCB, FPT 2024),
  and payouts from retained profit are `StockDividend`. The two adjust prices
  identically.

### The eight names swept in full (sprint 8)

The rows above were sourced to explain price-band breaches, so a cash dividend
too small to breach (1–5%) is mostly missing for every name. BCM, MSN, MWG,
PLX, POW, VNM, VPL and VRE had no action at all. Sprint 8 read **every**
entitlement notice VSDC lists for those eight, record dates 2023-04-01 →
2026-12-31, on 2026-10-21, and added 24 rows:
- cash dividends for BCM (3), MWG (5), PLX (4), VNM (8) and VRE (1);
- POW's rights issue, stock dividend and bonus of 2025-12-10;
- none for MSN, and none for VPL since its listing.

Notice by notice, the sources are in
[`sources/entitlements-eight-names.md`](sources/entitlements-eight-names.md).

[`corporate-action-coverage.csv`](corporate-action-coverage.csv) records that
sweep: one row per name, saying every action with an ex-date from
`complete_from` to `swept_on` is in the file (ADR-032). A dividend yield is read
only inside that stretch. Every other name has no row, so its yield is absent as
`dividend_history_incomplete` rather than understated. The `source` column is
last because it may hold commas.

## Venue history

[`venue-history.csv`](venue-history.csv) records when an instrument changed
venue, so each session is judged by its own venue's band and calendar
([ADR-028](../../docs/architecture/decisions/ADR-028-venue-history.md)). Each
instrument's spells must tile time: the first has no start, the last has no
end and is the venue the instrument is on now, and each begins on the session
the previous one ended. A name that never moved needs no rows.

| Security | Old venue | Last session there | First session on HOSE |
| -------- | --------- | ------------------ | --------------------- |
| BSR | UPCOM | 2025-01-06 | 2025-01-17 |
| MCH | UPCOM | 2025-12-17 | 2025-12-25 |

The spell boundary is the first HOSE session, so the sessions between the last
UPCOM trade and the first HOSE one fall in the UPCOM spell. They did not trade,
and they show as missing sessions, which is true. LPB (2020), DGC (2020) and
SHB (2021) moved before the data begins. VPL and TCX listed directly on HOSE.
Sources are in [`sources/venue-history-and-upcom-era-actions.md`](sources/venue-history-and-upcom-era-actions.md).

## Universes

Three sets in [`universes/`](universes/). `DEMO_INDEX` and `DEMO_EMPTY` are
invented and make no claim about a real index. `VN30` is real, sourced, and
**covers 5 August 2024 up to 18 September 2026 and nothing else** — read
outside that span it answers *unknown*, which is the point. Seeding today's
constituents and letting them stand in for earlier years would be the
survivorship bias this workstream exists to remove.

```
MarketData__UniverseDirectory=data/fixtures/universes
```

### VN30

Transcribed from HOSE's own constituent tables ("Công bố thông tin danh mục cổ
phiếu thành phần chỉ số VN30") for five reviews, plus one change between
reviews. Like the trading calendar, this is an exchange's public announcement,
not vendor data. HOSE's site served an empty page, so the tables were read from
copies of HOSE's documents hosted by Vietstock.

| Effective | Change | Announced | Basket source |
| --------- | ------ | --------- | ------------- |
| 2024-08-05 | no change from the previous basket; coverage starts here | 2024-07-15 | Kỳ 7/2024 table (HOSE, via Vietstock image) |
| 2025-02-03 | POW out, LPB in | 2025-01-20 | Kỳ 1/2025 PDF (HOSE), which states its period: 03/02/2025 to 01/08/2025 |
| 2025-08-04 | BVH out, DGC in | 2025-07-17 | Kỳ 7/2025 table (HOSE) |
| 2026-02-02 | BCM out, VPL in | 2026-01-21 | Kỳ 1/2026 list (HOSE PDF) |
| 2026-05-13 | DGC out, BSR in — extraordinary: DGC moved to the controlled list | 2026-05-07 | secondary (press); consistent with BSR heading the January reserve list |
| 2026-08-03 | PLX, TPB out; MCH, TCX in | 2026-07-15 | Kỳ 7/2026 table (HOSE) |

The quarterly reviews of October 2024, April 2025, October 2025 and April 2026
changed no members (April 2025 and October 2025 are primary; October 2024 and
April 2026 are secondary). No extraordinary replacement was found between
August 2024 and August 2025. The resulting baskets were checked by a script
against the five published tables: 30 names on every date, and each basket
equals the previous one plus the additions and minus the removals.

**One spell per stint, and `announced_on` only for a real inclusion.** A
security already in the index when coverage starts has a spell beginning on
2024-08-05 and a blank announcement date, because neither its true start nor
its announcement is known. The first transcription started every continuing
member on the old coverage start and gave it that review's date, which told a
point-in-time consumer an inclusion had been announced that never happened. The
six real inclusions (LPB, DGC, VPL, BSR, MCH, TCX) carry their dates.
`announced_on` means the admission announcement throughout. A removal's
announcement is in its own column, `removal_announced_on`. Each of the six
removals (POW, BVH, BCM, DGC, PLX, TPB) was published by the same review
announcement as the name that replaced it, so it carries that review's date
from the table above, with the same strength of evidence; a script checked
that every removal date equals its replacements' admission date. The
`DEMO_INDEX` rows are synthetic and leave it blank. A later file may fill a
blank date; it may not re-date one already recorded, and the import refuses
such a row whole.

Where the record is weaker than the membership itself:

- **Announcement dates.** 2024-07-15 and the October 2024 review's date rest on
  secondary sources only (the latter was not found, and nothing hangs on it:
  that review changed nobody). 2025-07-17 is the earliest date the announcement is
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
  "Sài Gòn Tài Lộc" in the July 2026 table, and LPB "Bưu điện Liên Việt" in
  January 2025 and "Lộc Phát Việt Nam" from April 2025; the current names are
  kept, and the import never overwrites a name anyway.

**What the first two-year VN30 backfill found in CafeF itself.** These are
properties of the source, not of this fixture, and are recorded here because the
VN30 backfill is what surfaced them. Every one is left as an open
`MissingSession` finding, because a missing bar is the truth about the series:

- **2025-02-03, 2026-01-29 and 2026-01-30** have no bar for any VN30 name. HOSE's
  own notices have all three as trading days: Tết 2025 closed 27–31 January and
  trading resumed on 3 February (Công văn 1046/SGDVN-GSTT), and the 2026
  calendar's only January closure is the 1st. The calendar is therefore left as
  it is. A calendar fitted to one vendor's gaps would stop reporting them.
- **2025-08-14** is missing for nine names (TPB, VCB, VHM, VIB, VIC, VJC, VNM,
  VPB, VRE) and present for the rest; asking CafeF again returns the same hole.
- **STB 2025-05-06** is served with a close below the day's low. The ingest
  rejects it as `InconsistentPrices`, so the session is missing rather than
  wrong.
- A gap makes the next close move across two or three sessions, which can
  exceed one session's band. Four such breaches (VJC and VPB on 2025-08-15, GVR
  and MWG on 2026-02-02) were dismissed with that reason. None is a corporate
  action.

**CTG's 2025 stock dividend is rounded to six places.** VSD publishes it as
"100 : 44,63658403", a ratio of 0.4463658403, and the import accepts at most
six decimal places, refusing a longer one rather than rounding it silently. It
is transcribed as 0.446366. The factor then differs by about 1×10⁻⁷, a
fraction of a đồng on a 35,000 price, well under one tick.

**VPB 2026-09-24 and HDB 2026-10-09 are from the issuers' announcements.**
Found the same way as the rest — two downward breaches (VPB −20.50%, HDB −20.00%)
raised by the October 2026 backfill — but sourced from the issuers' published
notices as reported on the day, not from a VSD notice: VPB's stock dividend of
10,000,000 : 2,604,104, ex 24 September, record 25 September (tinnhanhchungkhoan.vn,
21 September 2026), transcribed as 0.26041 for the six-place limit; HDB's 2025
dividend of 100 : 25 in shares and 100 : 5 bonus shares, ex 9 October, record 12
October (Vietnamplus, 1 October 2026; Mekong Asean for the ex-date). Restated,
both closes sit inside the band: VPB +0.2%, HDB +3.9%. Replace the source with
the VSD notice when it is transcribed.

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
