# ADR-028: Venue history — judging each session by the venue it traded on

**Status:** Accepted · **Date:** 2026-10-13 · **Phase:** Sprint 5, data reach (serves U3 and Phases 6–8)

## Context

Vietnamese issuers move: UPCOM to HNX to HOSE, and occasionally back. An
instrument keeps its identity across the move, which is what makes its series
one series, but the rules its sessions answer to do not. UPCOM bands at ±15%,
HNX at ±10%, HOSE at ±7%, and each venue has its own calendar.

Until now an instrument had one venue, its current one, and every session it
ever traded was judged by it. Sprint 5's backfill to July 2023 made the cost
visible. MCH traded on UPCOM until December 2025, and its UPCOM sessions raised
twelve price-limit breaches against HOSE's band, ten of them ordinary UPCOM
days. A transfer done with `pqt instrument transfer` changed the current venue
and erased the old one. Breadth over a venue counted a name that moved there as
having always been there.

## Decision

**An instrument's venue over time is recorded as spells, and every reader that
asks "which venue?" asks it for a session.**

- An `InstrumentVenueSpell` is `[effective_from, effective_to)` on one venue.
  A null start means "since before anything recorded" and a null end "still
  there". An exclusion constraint refuses two spells covering one session.
- An instrument with no spells is on its current venue for every session, so a
  name that never moved needs no rows and behaves exactly as before.
- Spells come from a sourced fixture, `data/fixtures/venue-history.csv`,
  imported at start-up after the symbol list and before corporate actions. An
  instrument's history is accepted whole or not at all. The spells must tile
  time: the first open at the start, each beginning where the last ended, and
  the last open at the end on the venue the master has the instrument on now.
  The import is append-only. A restatement is unchanged, and a contradiction
  is refused.
- Recording a history re-judges that instrument's open price-limit breaches on
  the venue each session traded on. One inside that venue's band is explained,
  saying so; one outside stays open.
- Readers: the quality inspector (band per bar, calendar per stretch), the
  adjustment engine's discontinuity check (band on the ex-date), the quote
  service (previous session, calendar and tick of the session's venue), and
  venue-wide breadth (members are the equities on the venue *that session*).

## Alternatives

- **Make `transfer` record the history.** It would answer for moves made from
  now on. It cannot state a move that happened before the instrument was
  seeded, which is every move in the backfilled span. It may be added later,
  writing the same spells.
- **Infer the move from the bars**, from a gap in the series or a change in
  move sizes. CafeF's BSR series starts three sessions after BSR's first HOSE
  session, so the gap is the vendor's and not the market's. The transcription
  rule that governs the calendar applies here too: the venue is the exchange's
  announcement, not a pattern in one vendor's data.
- **Judge every session by the widest band.** No false breaches, and a real
  HOSE breach of 12% would never be seen.

## Reasoning

The calendar, universe membership and corporate actions already follow the
same pattern: a sourced, append-only record of when something was true, read
for a session. Venue was the one property still read as "now". Leaving it so
meant the quality findings for every moved name were partly about the wrong
market.

## Trade-offs

- The sessions between a delisting from the old venue and the first session on
  the new one are assigned to the old venue's spell, so its calendar expects
  them. They did not trade, so they appear as missing sessions. That is true:
  there is no bar. They can be dismissed with that reason.
- Coverage counts in the registry still use the current venue's calendar. The
  three venues share their holidays except HOSE's January 2018 outage, so the
  counts are unaffected for every span PQT holds.

## Consequences

- `instrument_venue_spells` (migration `InstrumentVenueHistory`) holds the spells.
  `IVenueHistoryRepository` reads them in bulk, and `VenueTimeline` answers
  `On(session)` and splits a window into per-venue stretches.
- First rows: BSR (UPCOM until 2025-01-17, HOSE from it) and MCH (UPCOM until
  2025-12-25, HOSE from it), sourced in `data/fixtures/sources/`.
- A move made after today is recorded by adding its spells to the fixture
  together with `pqt instrument transfer`. Until both agree, the import refuses
  the history and says why.
