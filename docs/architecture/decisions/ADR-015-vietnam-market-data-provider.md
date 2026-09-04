# ADR-015: Vietnamese market data provider integration

**Status:** Accepted · **Date:** 2026-08-28 · **Phase:** Research Foundation Upgrade (U3)

> **One finding in this record is superseded.** The source-landscape table below
> concludes that free Vietnamese sources offer long-and-adjusted or
> short-and-raw and never long-and-raw, and that Gate A's corporate-action test
> is therefore unreachable. That is false. CafeF's date parameters work; they
> are `MM/dd/yyyy` and any other format is ignored in silence, which is how the
> endpoint appeared to cap at 65 sessions of history when it caps at 65 rows per
> request. See
> [ADR-021](ADR-021-raw-vietnamese-price-history.md).
>
> Everything else here stands: the layering, the licensing position, the
> provider selection model and the Vietcap adapter. The reasoning below is left
> exactly as it was written, per the convention in
> [README](README.md) — an ADR is not edited when a decision changes.

## Context

Phases 2, 3 and 4 built a market data pipeline: ingestion with provenance and
resume state, quality rules measured against a trading calendar and the venue's
price band, and corporate actions applied as a factor on read. All of it is
implemented, tested, and reviewed.

None of it has ever processed a real Vietnamese price.

The only market data in this repository is a six-session synthetic series for
`DEMO`, a ticker listed on no venue, written so the pipelines could be
demonstrated on a fresh clone. The trading calendar fixture is knowingly
incomplete — it carries four fixed-date holidays and not Tet — and calendar
import is therefore disabled by default in `docker-compose.yml`. The corporate
action fixture is invented and concerns only `DEMO`.

The consequence is precise and uncomfortable: every correctness property those
three phases claim is **designed and unit-tested but not empirically falsified**.
A normaliser that has only ever read its own fixture has not been tested against
a provider that sends a locale-dependent decimal separator, a null volume, a bar
dated on a holiday, or a symbol that quietly changed last quarter.

Continuing to build research infrastructure on top of an unvalidated pipeline
would mean discovering those bugs after several more phases depend on them.

## Decision

**U3 is mandatory.** It must not be downgraded, deferred, or satisfied by
fixtures. Synthetic data does not satisfy Gate A, and no claim about Phase 2–4
correctness may be made until real Vietnamese market data has passed through
them end to end.

The integration proceeds in two stages, both reusing seams that already exist:

1. **Bootstrap.** A collector writes CSV into the directory
   `FileMarketDataProvider` already reads. No backend change, real data
   immediately, and a reproducible artefact of exactly what the provider
   returned.
2. **Harden.** A native provider implementing the existing `IMarketDataProvider`
   port — `SourceCode`, `SupportedIntervals`, `FetchBarsAsync`, and
   transient-versus-permanent classification through
   `MarketDataProviderException.IsTransient`.

`FileMarketDataProvider` **remains permanently available** as the deterministic
fallback and test provider. It is not scaffolding awaiting removal.

The layering is enforced:

```
External provider
      ↓
Provider adapter / collector    ← provider names, units, symbology stop HERE
      ↓
Canonical PQT market data schema
      ↓
PIT · data quality · corporate actions
      ↓
Canonical research dataset
```

No provider-specific schema, field name, identifier or semantic may appear in
`PersonalQuant.Domain` or `PersonalQuant.Application`.

## Alternatives

**Continue on fixtures and integrate a provider later**, at Phase 5 or Phase 6.

**Integrate several providers at once** for redundancy from the start.

**Purchase a commercial feed immediately** and skip the evaluation.

## Reasoning

Deferring was rejected because the cost of an unvalidated pipeline compounds.
Each phase built on top of it inherits its unfound bugs and makes the eventual
correction larger. The pipeline is also at its most correctable now, while
nothing depends on its output.

Integrating several providers at once was rejected as premature. One provider
proves the adapter boundary; a second proves it is genuinely replaceable, and
that is worth doing — but after the first one has found the bugs, not
simultaneously with it.

Purchasing a feed before evaluation was rejected because the questions in this
ADR are the point. A feed that cannot be stored, or whose corrections are
published by silently rewriting history, is unusable for backtesting no matter
what it costs.

The two-stage approach exists because the existing file provider is genuinely
useful rather than a placeholder. It turns "get real data flowing" into a
collector script, and it leaves a permanent deterministic path for tests. That
is a better outcome than a native adapter that becomes the only way to run the
system.

## Provider capability matrix

Candidates to evaluate. **Every cell starts at `VERIFY` and may only change once
the capability has actually been checked against the provider.** A cell is never
marked supported on the strength of documentation, a blog post, or a third-party
client's feature list.

| Capability | vnstock | SSI | TCBS | FiinQuant / FiinPro | HOSE / HNX |
| --- | --- | --- | --- | --- | --- |
| Daily OHLCV | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Intraday | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Corporate actions | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Index / universe constituents | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Historical data depth | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Historical revisions | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Point-in-time / as-of availability | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Trading calendar | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Dividends · splits · rights issues, typed separately | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Delistings and ticker changes | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Rate limits | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Authentication | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| API stability | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Licensing / storage rights | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Redistribution rights | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |
| Commercial-use rights | VERIFY | VERIFY | VERIFY | VERIFY | VERIFY |

The matrix is maintained in this ADR and updated as cells are verified. A
provider is not selected until the rows it will be relied on for are filled in.

**No capability cell has moved.** Everything below was answered from published
licence text and documentation, which is the half of the evaluation that can be
done without touching an endpoint. The capability rows stay `VERIFY` until the
first real call is made, and that call is what this section exists to permit.

---

## Selection · 2026-08-30

**Constraint stated by the operator:** free, continuously updatable, no broker
account. That removes SSI FastConnect, FiinQuant and any commercial feed from
consideration, and leaves the free endpoints published by Vietnamese brokers
for their own web front ends.

### Decision — a native adapter, no third-party client

PQT calls the source's public endpoint directly through an adapter implementing
`IMarketDataProvider`. It does **not** take a dependency on `vnstock` or any
other wrapper.

Three reasons, in order of weight:

1. **Licence containment.** `vnstock` is published under a custom licence for
   personal, non-commercial use: *"use of Vnstock for commercial purposes by
   any organization is prohibited"*, extending to *"activities where Vnstock
   directly or indirectly contributes to generating revenue or cash flow for an
   organization without written approval"*. Personal and academic research is
   free of charge, which covers this repository today — but a client library
   whose terms turn on what the *user* later does is a licence attached to
   PQT's future, and this project is proprietary. Reaching the same endpoints
   directly leaves only the data question, which has to be answered either way.
2. **The parsing rule.** Prices are parsed as `decimal` from the wire text. A
   wrapper that has already deserialised through a `double` — which is what a
   pandas-shaped client does — has lost the exact value before PQT sees it, and
   a close that comes back a fraction different compounds into returns the
   market never produced.
3. **The layering.** Provider names, units and symbology stop at the adapter.
   A client library that returns its own frame shape moves that boundary into
   whatever consumes the frame.

`vnstock` remains the better tool for interactive exploration, and nothing here
argues otherwise. It is a dependency decision, not a quality judgement.

### The seven questions, answered

| # | Question | Answer |
| --- | --- | --- |
| 1 | **API / client availability** | Yes. The endpoints a broker serves its own web charts from are reachable without a key or an account. |
| 2 | **Underlying source** | A Vietnamese broker's public market-data endpoint — not the exchange. The prices originate at HOSE/HNX/UPCOM and reach PQT second-hand. |
| 3 | **Data ownership** | The exchange, and the broker republishing it. Not PQT, and not any client library. |
| 4 | **Licensing / terms of service** | **No published grant found.** These are undocumented endpoints serving a public web front end, not a documented API with terms. Nothing states that retention is permitted, and nothing states that it is forbidden. |
| 5 | **Storage rights** | **Not granted in writing.** PQT retains data locally, for personal research, on the operator's own machine. That is a position taken with the absence of a grant understood, not a right this ADR claims to hold. |
| 6 | **Redistribution rights** | **Not granted, and therefore not exercised.** No real market data enters this repository, no export leaves the operator's machine, and no chart built from it is published. |
| 7 | **Historical revision behaviour** | **Unknown, and assumed to be silent rewrite.** No restatement or correction feed is published. `quant.bar_revisions` is therefore PQT's *only* record of what changed and when. |

### What answer 7 costs, stated plainly

The selected source declares `ReportedFields.Restatements = false` and
`ReportedFields.AnnouncementDates = false`, and both are load-bearing:

- **U4 strict mode has no announcement dates from this source.** Corporate
  actions must come from somewhere that publishes them, or strict mode excludes
  every action with a null `announced_on` and a backtest sees no adjustments at
  all. That is the honest failure and it is the one U4 has to be built against.
- **Reproducibility rests entirely on U1.** A source that silently rewrites
  history cannot be replayed. The observation history is not a nice-to-have
  here; it is the only thing standing between a corrected close and a backtest
  that quietly changes its answer between two runs.

### Rules this selection is bound by

1. **Fixtures stay synthetic.** Nothing fetched is committed. `data/` keeps the
   invented `DEMO` series, and the real series lives in a git-ignored directory
   and in the operator's database.
2. **One ticker, then a decision.** The first ingest is `FPT` on HOSE over five
   years. Widening to the rest of HOSE is a separate decision made after the
   pipeline has been falsified against real data — not a consequence of this
   one.
3. **Rate limits are respected by the existing call limiter**, not by a new
   mechanism. The guest tier on these endpoints is measured in tens of requests
   per minute; a five-year daily backfill for one ticker is a handful of calls.
4. **A licence change stops the path.** If terms appear that forbid retention,
   [`../data-policy.md`](../data-policy.md) governs: stop ingestion, delete,
   record. Nothing about the pipeline depends on this source specifically —
   that is what the adapter boundary is for.
5. **This is not a commercial-use grant.** If PQT ever generates revenue, the
   data question is reopened before anything else happens.

---

## Verified · 2026-08-30 · the free feeds serve adjusted prices

The first calls were made. They answered one capability question decisively and
it is the one that matters most, because PQT's entire Phase 4 shape is *raw
prices, adjusted on read*.

`FPT`, four sessions from 10–14 January 2022, daily:

| Source | Endpoint | Close on 11/01/2022 | Raw or derived |
| --- | --- | --- | --- |
| Vietcap (VCI) | `trading.vietcap.com.vn/api/chart/OHLCChart/gap-chart` | `44810.34` | **Adjusted** |
| DNSE | `api.dnse.com.vn/chart-api/v2/ohlcs/stock` | `44.81` (thousands) | **Adjusted** |
| SSI iBoard | `iboard-api.ssi.com.vn/statistics/charts/history` | `44.81` (thousands) | **Adjusted** |
| CafeF | `cafef.vn/du-lieu/ajax/.../pricehistory.ashx` | `GiaDongCua` raw **and** `GiaDieuChinh` adjusted, side by side | **Both** |

Three independent brokers return the same number to the hundredth. The
fingerprint is the value itself: `44810.34` is a fraction of a dong, and HOSE
trades `FPT` on a 50-dong tick. **No such price ever traded.** It is a
back-adjusted number, and the agreement between three sources is agreement
about a derivation, not corroboration of a fact.

`accumulatedValue` is also `null` on those older bars, so turnover is not
available for history from VCI even where prices are. Where it is present its
unit is undocumented and appears to be millions of dong; an inferred unit gives
a turnover wrong by a factor of a million if the inference is wrong, which is
worse than an absent one, so the field is not mapped and the capability declares
`Turnover = false`.

### The volume is one book, not two

Vietnamese venues run two of them. Continuous order matching — *khớp lệnh* — is
one; negotiated block trades — *thỏa thuận* — are agreed off the book and
reported separately. **VCI's `volume` is the matched book only.**

This is recorded rather than merely noted, as
`ProviderReportedFields.VolumeBasis`, for the same reason
`AdjustsPricesAtSource` is: the two numbers are indistinguishable once stored.
A series carrying matched-only volume and one carrying the sum have the same
shape, the same column and the same plausible magnitudes, and nothing
downstream can tell them apart by inspection.

The consequence is not cosmetic. Block trades are where institutional size
actually moves, so a matched-only volume understates traded size by whatever
proportion of the day went through negotiated — and understates it worst on
exactly the days a liquidity filter is deciding something. A universe screened
on average daily volume, a participation-rate cap, an execution-cost model: each
means something different depending on this value, and none of them can detect
which it was given.

`VolumeBasis.Unspecified` is the default and is not a synonym for *everything*.
A directory of CSV files exported by somebody else genuinely does not know what
its volume counts, and reading that silence as a claim is how an unstated basis
becomes an assumed one.

**Mixing bases is not yet refused, and that is deliberate.** Ingestion refuses
to mix a raw series with a source-adjusted one because two registered sources
declare opposing adjustment conventions and the mixture is reachable today. Only
one source states a volume basis at all, so a rule against mixing would guard a
case that cannot occur. When a second source states a different basis, the
refusal belongs beside V9 in the ingestion service, built the same way.

### Why this blocks storing them as bars

`quant.bars` holds what the market printed. The adjustment layer multiplies it
on read, by a factor derived from recorded corporate actions. Feeding it a
series that is *already* adjusted means adjusting twice, and the second
adjustment is invisible: the numbers stay plausible, the returns stay smooth,
and every backtest over the range is wrong by the product of every factor since.

This is exactly the situation `ProviderLimitations.AdjustsPricesAtSource`
exists to declare and rule V9 exists to refuse. The declaration is not
paperwork — it is the difference between a source that can populate this schema
and one that cannot.

### The tension this creates, stated rather than resolved

| | Long history (5 years) | Raw prices |
| --- | --- | --- |
| VCI / DNSE / SSI | ✔ verified back to 2022 | ✘ adjusted at source |
| CafeF | ✘ the endpoint caps at 65 sessions | ✔ raw close published beside the adjusted one |

Free sources offer **long-and-adjusted** or **short-and-raw**, and U3 asked for
long-and-raw. That is a finding about the Vietnamese free-data landscape, not a
bug to code around, and it is precisely the class of thing this ADR predicted:
*"a feed that cannot be stored ... is unusable for backtesting no matter what
it costs."*

It also disarms one of U3's two acceptance tests. A known bonus or split
produces **no discontinuity** in an adjusted series, so an adjusted feed cannot
falsify the corporate-action engine — there is nothing left in the numbers for
the engine to explain.

**Decided by the operator:** take the adjusted series as a separate declared
dataset, and keep looking for raw history in parallel.

> **Superseded 2026-09-04.** The raw history was found, in the source this very
> table rules out. CafeF's `StartDate` and `EndDate` are honoured in
> `MM/dd/yyyy` and ignored without complaint in any other format; the 65-session
> cap bounds one response, not the history behind it, and a quarter-sized window
> paginates in full back to listing in 2006. `FPT` on 27 May 2016 shows a
> −13.68% gap in the raw close against +1.41% in the adjusted one — a move the
> ±7% band makes impossible, which is the discontinuity this paragraph says
> cannot be obtained. See [ADR-021](ADR-021-raw-vietnamese-price-history.md).

---

## First real ingest · 2026-09-01

`FPT`, daily, 2021-12-27 to 2026-08-28. **1,164 bars, 0 rejected, 0 revised.**
A second pass stored 0 and revised 0, so the checkpoint and the storage key are
idempotent against a real source and not only against a fixture.

The run was bounded to one ticker by configuration. Reference-data seeding alone
puts ten HOSE equities in the master, and an unrestricted first pass would have
fetched and stored five years of a third party's data for nine securities nobody
asked about.

### What the run proved

| Claim | Evidence |
| --- | --- |
| Prices survive as decimals | Stored closes carry six decimal places — `93017.520000` — exactly as the source sent them |
| Point-in-time reads work on real data | `knownAsOf` one second before the observation instant returns **0 bars**; one second after returns the series. No fallback to the current value |
| A source-adjusted series is not adjusted again | The read reports `adjusted: true`, `adjustedAtSource: true`, `adjustedBars: 0`, and every price factor is 1 |
| Turnover is honestly absent | `turnover: null` throughout, as the capability declares |
| Quality rules fire on real data | One `MissingSession` finding, discussed below |

### Two defects the real data found, which no probe had

**The endpoint compresses large responses whether or not the request asks it
to.** A three-day probe comes back as plain JSON; a five-year request comes back
gzipped. The adapter read the bytes as text, the parse failed, and the run was
recorded as a provider failure. Every capability probe up to that point had been
small enough to miss it. `HttpClientHandler.AutomaticDecompression` is now
configured, and the failure message names the cause.

**The calendar was wrong, and the pipeline said so.** The one quality finding
was a `MissingSession` for Friday 2 January 2026: the calendar expected a
session and no bar existed. Three independent sources — Vietcap, DNSE and SSI —
have no bar for that date either, which made a provider gap unlikely.

The exchange's annual notice, published in December 2025, listed only 1 January
as closed. The government then swapped Friday 2 January to Saturday 10 January,
and the exchange announced the extra closed session separately. The transcribed
calendar predated that announcement.

The correction was taken **from the later announcement, not from the data**.
Fitting the calendar to the observed sessions would have destroyed the only
thing that makes it useful: it is the independent statement against which a
missing session is judged. A calendar derived from the bars cannot find a
missing bar.

**58 of 59 closed sessions across five years agreed with the data on the first
attempt, and the one disagreement was the calendar's fault.** That is the
strongest available evidence that both are right.

The finding itself remains **open**. Nothing in this system closes a finding
automatically, and the operator surface that would let a human close one is the
CLI U3 still owes.

## Seven questions that must be kept separate

Conflating any two of these is how a project acquires data it has no right to
hold.

1. **API / client availability** — can the code call it?
2. **Underlying provider or source** — whose systems actually serve the bytes? A
   Python client is a *client*; the broker or exchange behind it is the *source*.
3. **Data ownership** — who owns the data itself?
4. **Licensing and terms of service** — what do the terms actually permit?
5. **Storage rights** — may it be retained, and for how long? A feed that permits
   display but not retention rules out backtesting outright.
6. **Redistribution rights** — may it leave this system in any form, including a
   chart, an export, or a repository?
7. **Historical revision behaviour** — are corrections published, or is history
   silently rewritten? A feed that rewrites history makes reproducible backtests
   impossible.

> **Technical availability is never permission to store or redistribute.**

An open-source client library says nothing about the rights to the data it
fetches. The licence on a Python package and the terms governing the endpoint it
calls are two different questions with two different answers, and only the
second one determines what PQT may keep.

No provider is integrated until all seven are answered **in writing, in this
ADR**, per the checklist in [`../data-policy.md`](../data-policy.md), which
remains the governing document for market data licensing.

## Trade-offs

- Real data introduces a dependency on a source that can change or disappear. The permanent file-provider fallback bounds that.
- Evaluating licensing properly is slow, and the temptation is to call an endpoint first and read terms later. That order is prohibited here.
- The two-stage approach means writing a collector that is later partly superseded. That cost is small and buys real data weeks earlier.
- A single provider is a single point of failure for data quality. Cross-checking against a second source is desirable and is not required for Gate A.

## Consequences

- **Gate A cannot be passed with fixtures.** Real Vietnamese market data must pass through Phases 2–4 end to end.
- A **real Vietnamese trading calendar including Tet** must be imported, and calendar import enabled. Until then, completeness scoring is unmeasured rather than measured.
- Quality findings must be generated **from real data**, not only from unit tests.
- A bulk-load path is required, because backfilling real history through row-by-row inserts is impractical.
- Telemetry on the ingestion path is required, because a pipeline running against a live source without observability is a pipeline nobody can debug.
- Contract tests run against **recorded** responses, never against a live endpoint in CI.
- Fixtures added under `data/` must remain synthetic or trivially small. A recognisable vendor extract is a licensing incident, per [`../data-policy.md`](../data-policy.md).
- **Selected:** a free public broker endpoint, reached by a native adapter, with no third-party client library. Recorded in *Selection · 2026-08-30* above, together with the seven answers. A change of provider after this is a new ADR, not an edit to that section.
- **The capability matrix is still unverified.** No cell may move until a real call has been made and its answer checked. What the selection section settles is licensing, which is the part that must be settled *first*.
- **Storage rests on an absence rather than a grant.** That is recorded rather than smoothed over, and it bounds what this data may ever be used for: personal research, locally, never redistributed.
