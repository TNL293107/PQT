# ADR-021: Raw Vietnamese price history is obtainable, and CafeF is the source

**Status:** Accepted · **Date:** 2026-09-04 · **Phase:** Research Foundation Upgrade (U3)

**Supersedes the source-landscape conclusion of
[ADR-015](ADR-015-vietnam-market-data-provider.md).** That ADR's decisions about
provider layering, licensing and the Vietcap adapter all stand. What this record
replaces is one finding inside it — the claim that free Vietnamese sources offer
*long-and-adjusted* or *short-and-raw* and never long-and-raw — which is now
known to be false, and which was blocking Gate A.

## Context

ADR-015 recorded a tension it deliberately did not resolve:

| | Long history | Raw prices |
| --- | --- | --- |
| VCI / DNSE / SSI | ✔ verified back to 2022 | ✘ adjusted at source |
| CafeF | ✘ *the endpoint caps at 65 sessions* | ✔ raw close beside the adjusted one |

and drew the consequence that mattered:

> It also disarms one of U3's two acceptance tests. A known bonus or split
> produces **no discontinuity** in an adjusted series, so an adjusted feed
> cannot falsify the corporate-action engine.

Gate A requires exactly that test. On the strength of this table it was recorded
as unreachable from free sources — not merely unmet, but impossible — and the
operator's decision was to take the adjusted series as a separate dataset and
keep looking for raw history in parallel.

**The italicised half of that table was wrong.** The cap is real; the conclusion
drawn from it was not.

## What was actually measured

`cafef.vn/du-lieu/ajax/pagenew/datahistory/pricehistory.ashx` accepts
`Symbol`, `StartDate`, `EndDate`, `PageIndex` and `PageSize`.

**The date parameters work. They are `MM/dd/yyyy`, and any other format is
ignored in silence.** That is the whole error. A probe written in Vietnamese
date order gets a well-formed `200` with `Success: true` and a plausible body,
and every window it asks for returns the same 63 recent sessions — which reads
exactly like an endpoint that has no date filter.

The same window, three ways:

| `StartDate` … `EndDate` | `TotalCount` | Newest row |
| --- | --- | --- |
| `01/07/2026` … `31/07/2026` | 63 | 03/09/2026 — ignored |
| `2026-07-01` … `2026-07-31` | 63 | 03/09/2026 — ignored |
| **`07/01/2026` … `07/31/2026`** | **23** | **31/07/2026** — honoured |

**The 65-session cap is per request, not per history.** Asking for all of 2018
returns `TotalCount = 65` and paginates through 01/10–28/12/2018 only: the
window's most recent 65 sessions, with the rest of the year dropped and nothing
said. Asking for one quarter returns `TotalCount = 57` and paginates through
the whole of it, 02/01–30/03/2018. A quarter fits under the cap, so walking
quarter by quarter reaches arbitrarily far back.

**It reaches to listing.** A request for all of 2006 returns 13 sessions; FPT
listed on HOSE on 13 December 2006.

**The raw series is raw.** `GiaDongCua` diverges from `GiaDieuChinh` by the
cumulative factor, and the divergence grows going back — at the end of 2006,
raw 460 against adjusted 11.07. The ratio falls every year from 8.738 at the end
of 2013 to 3.876 at the end of 2017, which is what a security paying a stock
dividend most years looks like when one series is adjusted and the other is not.

**It carries the discontinuity the engine has to explain.** Scanning Q2–Q3 2016
day by day, 125 sessions, produces exactly one day where the two series
disagree:

```
DATE          RAW      RAW %     ADJ     ADJ %
27/05/2016     41    -13.68%    8.62    +1.41%
```

The raw close gaps down 13.68 per cent; the adjusted close is continuous and
rises 1.41 per cent. A 13.68 per cent move is impossible on HOSE under the ±7
per cent daily band, so it is not a price move at all — it is an entitlement
detaching, and it is precisely the `PriceLimitBreach` that Phase 3 raises and
Phase 4 must account for.

## Decision

1. **Raw Vietnamese price history is obtainable from free sources.** ADR-015's
   landscape finding is superseded. The Gate A acceptance test — one known
   bonus, rights issue or split reproduced through the corporate-action engine —
   is reachable, and 27 May 2016 on `FPT` is a specific, located instance of it.

2. **CafeF becomes a second registered provider**, declaring
   `AdjustsPricesAtSource = false`. It is the raw source the schema was designed
   for: `quant.bars` holds what the market printed, and the adjustment layer
   multiplies it on read.

3. **The two sources may not meet in one series, and nothing needs building to
   prevent that.** Rule V9 already refuses a source-adjusted provider writing
   into a series a raw provider holds, and refuses it in both directions. VCI's
   `FPT` daily series and a CafeF `FPT` daily series are, correctly, mutually
   exclusive — the existing refusal is what makes adding this source safe rather
   than dangerous.

4. **The elided endpoint path and the date format are recorded here**, in full.
   ADR-015 wrote the URL as `cafef.vn/du-lieu/ajax/.../pricehistory.ashx` and
   recorded no parameters, so re-deriving the finding meant re-deriving the
   whole endpoint. A capability claim that cannot be re-tested from the record
   is a claim that gets believed for longer than it is true.

## Alternatives

**Leave ADR-015's finding standing and keep looking for a paid raw feed.**
Rejected. The finding is false and it was gating Gate A, which gates Phase 5.
Paying for what a free source already serves is a cost with no capability
attached.

**Edit ADR-015 in place.** Rejected, by this repository's own convention: an
ADR is not updated when a decision changes, so the reasoning at the time is
preserved. The mistaken conclusion is worth keeping visible precisely because
the *way* it was reached — a silent fallback that returns a plausible answer —
is the second instance of that failure mode in this workstream.

**Take CafeF as the only source and drop Vietcap.** Rejected for now. VCI is
verified, holds five years of `FPT` already ingested, and serves a dataset that
is legitimate on its own terms. Two declared datasets that cannot mix is the
model this system is built on; collapsing to one source would discard working
data to simplify a decision that is already made.

## Reasoning

The failure mode is worth naming, because it is the same one that cost two weeks
earlier in U3. The Vietcap adapter read gzipped bytes as text and failed only on
a large request, having survived every small probe. Here a wrongly-formatted
date is not rejected — it is dropped, and the endpoint answers the *default*
question with a body indistinguishable from a correct answer.

Both are silent fallbacks. Both produce a plausible result that a probe cannot
tell from a real one. In both cases the only thing that would have caught it is
a **discriminating** test: one whose two outcomes differ under the hypothesis.
Asking for a window inside the default range — where a working filter must
return fewer rows than the default — settles in one request what four requests
against out-of-range windows could not.

## Trade-offs

**Cost per ticker.** Roughly 80 quarterly windows from 2006 to today, about
three pages each, so around 240 requests for one instrument's full history.
Under the existing call-spacing policy that is minutes, not seconds, and the
ingestion checkpoint already makes it resumable across runs.

**A second symbology and a second parser.** CafeF returns Vietnamese field
names, `dd/MM/yyyy` dates in the body and `MM/dd/yyyy` in the query, decimal
commas inside `ThayDoi`, and prices in thousands of dong. All of it stops at the
adapter, as ADR-015 requires, but it is a real adapter and not a variation on
the existing one.

**Two sources that cannot mix.** Every `FPT` daily series in this system belongs
to VCI or to CafeF and never to both. Migrating an existing series from one to
the other is not an ingestion; it is a decision about which dataset is
canonical, and there is no mechanism for it. That is the correct cost of the
guarantee, and it is why the choice matters before the backfill rather than
after.

**A source that may change without notice.** A public web endpoint carries no
contract. The declared capability is what this deployment measured on this date,
and `ProviderCapability` exists to record exactly that.

## Consequences

- Gate A's corporate-action test is reachable. It is not yet passed: the action
  for 27 May 2016 is not recorded and nothing has been run through the engine.
  The adapter exists as of this record's commit, declaring
  `AdjustsPricesAtSource = false`, `VolumeBasis = MatchedAndNegotiated`,
  `Turnover = false` and `MaxPeriodsPerCall = 65`.
- **The declared call bound had to start being enforced.** Every source before
  this one declared none, so V10 was written down, rendered and applied by
  nothing. Against a source that truncates a long response rather than refusing
  it, that gap loses bars from the middle of a range the run records as covered.
- **The adapter refuses a row outside the window it asked for.** Dropping such
  rows would turn the silent-fallback bug into an empty range recorded as
  covered — the same failure one layer down. The date format is written once, in
  the adapter, and checked against every row that comes back.
- ADR-015 stays as written. Its landscape table is superseded by this record and
  a reader who finds it first must be able to get here — the index and the
  roadmap carry the pointer.
- The licensing position of ADR-015 applies unchanged. CafeF publishes no terms
  forbidding retrieval; storage rests on the absence of a prohibition rather
  than on a grant, and redistribution stays out of the question. If PQT ever
  earns revenue, this reopens first.
- `ProviderReportedFields.VolumeBasis` gains a second declarant. CafeF reports
  `KhoiLuongKhopLenh` and `KLThoaThuan` separately, so it can serve
  `MatchedAndNegotiated` and a real turnover — both of which VCI cannot. When it
  does, a volume-basis mixture becomes reachable for the first time, and the
  refusal that guards it belongs beside V9.
