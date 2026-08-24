# Trading calendar CSV

The interchange format the file-backed trading calendar source reads.

Configure it by pointing `MarketData__TradingCalendarPath` at the file, then
run an import. With that setting blank, no calendar source is registered.

## Why this is imported rather than seeded

Vietnam's exchange calendar cannot be derived:

- **Tet** and the **Hung Kings commemoration** follow the lunar calendar, so
  their Gregorian dates move every year.
- **Substitute days** for holidays that fall at a weekend are set by an annual
  government decree, not by a rule.

Nothing is seeded in their place, and that is deliberate. A partial calendar is
worse than none: with the four fixed-date holidays recorded and Tet absent, the
system would believe its calendar covers the year and report a week of real
closures as missing sessions. With no calendar at all it reports completeness
as unmeasured, which is true.

Read `calendarIsComplete` on the quality report before reading any completeness
figure.

## Columns

A header row is required. Columns are matched **by name**, not by position.

| Column     | Required | Meaning                                     |
| ---------- | -------- | ------------------------------------------- |
| `exchange` | yes      | The venue's operating code                   |
| `date`     | yes      | The closed date, `yyyy-MM-dd`                |
| `name`     | yes      | What the closure is                          |

```csv
exchange,date,name
HOSE,2026-01-01,New Year's Day
HOSE,2026-02-17,Tet
HNX,2026-02-17,Tet
```

One row per venue per date. Vietnamese venues currently close together, but
that is an observation about today's market rather than a rule — a
venue-specific closure such as a systems outage has to be expressible without
inventing a national holiday.

**Weekends are not listed.** Saturday and Sunday are structural for every venue
this system covers, and recording them would be tens of thousands of rows
asserting what the calendar already knows.

## What an import will and will not do

**Additive only.** A closure already recorded is left alone. A closure absent
from the file is *not* removed — a truncated calendar must not silently reopen
a market.

**A row naming a venue the system does not hold is rejected** with a reason,
and the rest of the file is still imported. Seed the exchange first.

**A row with an unreadable date fails the whole import.** Skipping it would
move the calendar's horizon past a date whose closure was never recorded, and
every session in it would then read as missing — the exact failure the calendar
exists to prevent.

## How the calendar is used

Two of the three quality rules rest on it:

- **`MissingSession`** — a day the calendar says the venue traded, with no bar
  stored for it.
- **`UnexpectedSession`** — a bar on a day the calendar records as closed.
  Usually the calendar being wrong rather than the data, and worth knowing
  either way.

Both are skipped entirely when the calendar does not cover the window under
inspection, so an unpopulated calendar produces no findings rather than a few
hundred false ones.

## Licensing

The Vietnamese holiday schedule is published by the government and is not
licensed market data. A vendor's *exchange* calendar may be — check before
committing one, and note that `data/raw/`, `data/cache/` and `data/external/`
are ignored by Git precisely so a licensed dataset cannot be added by accident.

A deliberately incomplete synthetic fixture lives in
[`../fixtures/trading-calendar.csv`](../fixtures/trading-calendar.csv).
