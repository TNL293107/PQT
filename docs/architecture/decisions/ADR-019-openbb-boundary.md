# ADR-019: OpenBB as a deferred, out-of-process data option

**Status:** Accepted · **Date:** 2026-08-29 · **Phase:** —

## Context

OpenBB's Open Data Platform is the largest open financial-data connector
project available: 32 provider packages, 15 data routers, 180 standardized
dataset models, and one core reachable four ways — Python, REST, CLI and an MCP
server. It is the obvious thing to ask about when a project needs data breadth,
and it was raised as a candidate for PQT.

A decision is needed **before** any evaluation branch is cut, for the same
reason [ADR-017](ADR-017-qlib-research-adapter.md) needed one before any Qlib
code was written: the difference between an external ecosystem behind a seam
and an external ecosystem in the middle is nearly invisible in the first module
and nearly irreversible by the tenth.

Three facts, established on 2026-08-29 and recorded in
[`../openbb-evaluation.md`](../openbb-evaluation.md), decide it:

1. OpenBB is `AGPL-3.0-only`. This repository is proprietary, all rights
   reserved ([ADR-007](ADR-007-private-proprietary-repository.md)).
2. **No Vietnamese equity-market provider was identified in the reviewed
   provider catalogue.** Vietnam appears only at macro and country level, in
   the country lists of `imf`, `econdb`, `tradingeconomics`, `fmp` and `bls`.
3. **No point-in-time, revision, observation-time, dataset-versioning or
   manifest-hashing model was identified** in the reviewed repository or
   documentation — none of U1, U4 or U5.

## Decision

**OpenBB is `DEFERRED`: optional, out-of-process, operator-installed, and never
a PQT dependency.**

No OpenBB code is vendored, imported, packaged, containerised or linked by PQT.
Specifically prohibited:

- No `openbb` package in `quant/pyproject.toml`, in base dependencies or in any
  optional extra.
- No OpenBB service in `docker-compose.yml`.
- No `IMarketDataProvider` implementation written against OpenBB. That would
  put an AGPL client inside the PQT process, which is the arrangement this ADR
  exists to prevent.
- No copying of OpenBB's `standard_models` field definitions. The catalogue may
  be read as a checklist of dataset shapes worth having; the definitions are
  AGPL source.
- No OpenBB type in any PQT domain, application or serialised contract.
- PQT is never built as an OpenBB Workspace backend.

**If OpenBB is ever used, it runs as a Tier 2 external collector outside PQT,
and its output is adapted to the existing file-ingestion contract** — the
stage-one bootstrap [ADR-015](ADR-015-vietnam-market-data-provider.md) already
defines. The adapter lives outside this repository.

That contract is concrete, and was checked against
`backend/src/PersonalQuant.Infrastructure/MarketData/FileMarketDataProvider.cs`:

```
<root>/<interval>/<TICKER>.csv
columns read by header name:
  required  timestamp · open · high · low · close · volume
  optional  turnover
```

An OHLCV frame maps onto it directly, so **no backend change is required**. Two
consequences follow and are recorded rather than glossed:

- Files are keyed by ticker, so alias resolution to the canonical instrument ID
  remains the Phase 1 / [ADR-012](ADR-012-identifier-aliases-and-provider-import.md)
  path and is not bypassed.
- **Provenance gap.** The file-ingestion path records `SourceCode = FILE`, so
  the OpenBB and upstream-provider origin is **not preserved at bar level**. In
  a project whose central argument is data lineage that is a real defect, not a
  detail. **Resolving this provenance gap is a prerequisite for any sustained
  Tier 2 use.** How it is resolved is out of scope here, it is named in the
  trigger below, and nowhere in this repository is it assumed solved.

### Trigger to revisit

All three must hold:

1. PQT needs a non-Vietnamese series — a global benchmark, a macro factor, an
   FX rate — that no Vietnamese provider supplies.
2. The operator has separately assessed OpenBB's own licence terms **and** the
   terms of whichever data providers OpenBB would be configured against.
3. The bar-level provenance question above has been answered.

### What is taken anyway, at zero dependency cost

Four architectural ideas, each landing on an existing phase or workstream. None
creates a new phase, a new workstream, or a dependency:

| Idea | Lands in |
| --- | --- |
| Provider capability and coverage metadata, declared rather than discovered by failure | U3 |
| Coverage introspection — "what data does PQT hold for this instrument" | Phase 5 |
| One application core, many interfaces; no interface holds business logic | architectural rule |
| MCP tool categories with progressive activation | Phase 18 |

## Alternatives

**OpenBB as a PQT dependency** — install it in `quant/`, call it from the
research layer, take the provider breadth directly.

**An OpenBB-backed `IMarketDataProvider`** — a native provider inside
`PersonalQuant.Infrastructure` wrapping the OpenBB Python SDK or its REST API.

**PQT as an OpenBB Workspace backend** — expose `/widgets.json` and
`/apps.json` and render PQT data inside OpenBB's UI instead of building a
terminal.

**Adopt OpenBB's `technical`, `quantitative` and `econometrics` extensions**
rather than writing indicators and statistics in Phase 7.

**Reject OpenBB outright**, taking neither the data path nor the ideas.

## Reasoning

Three independent grounds support deferral, and **any one of them is
sufficient**. That redundancy matters: if one weakens later — a Vietnamese
provider appears, say — the decision does not automatically flip.

**Licence.** OpenBB is `AGPL-3.0-only`; this repository is proprietary and all
rights reserved. Out-of-process, operator-installed use keeps OpenBB from
becoming a PQT dependency and removes the coupling entirely — **but it is not a
legal clearance.** An operator who chooses to run OpenBB is responsible for
assessing the AGPL themselves, and separately for the terms of the data
providers it connects to. This ADR records an engineering boundary, not a
compliance opinion, and takes no position on derivative works or on AGPL §13.
[ADR-017](ADR-017-qlib-research-adapter.md) recorded Qlib's MIT terms as
imposing no copyleft obligation and treated that as decision-relevant; applying
the same input here points the other way, and that is enough.

**Coverage.** OpenBB cannot satisfy U3 or Gate A. The single piece of work on
PQT's critical path — real Vietnamese market data through Phases 2 to 4 — is
exactly the piece OpenBB does not address. An external ecosystem that does not
help with the current bottleneck is not urgent, whatever else it offers.

**Correctness.** With no such model identified, no OpenBB output could enter a
canonical dataset without passing PQT's own normalisation, validation,
point-in-time and provenance stages anyway. This is not a criticism of
OpenBB — a connector layer has no business inventing a temporal model — but it
does mean the integration saves less than it appears to. The expensive part of
adding a data source in PQT is never the fetch.

**The dependency alternative was rejected on all three grounds at once,** which
is unusual and is why this decision was not close.

**The `IMarketDataProvider` alternative is the tempting one and is rejected
specifically.** It looks like good architecture — it uses the existing seam,
which is what the seam is for — but it inverts the licence position by putting
an AGPL client in the PQT process. The seam is not the problem; which side of
the process boundary the code sits on is.

**The Workspace backend alternative was rejected on product direction.** PQT
exists to be a Bloomberg-inspired terminal for Vietnamese markets. Rendering
PQT's data inside OpenBB's UI would make PQT a data plugin in someone else's
product, which is the opposite of the goal, and would put PQT's interaction
model at the mercy of an upstream roadmap.

**The analysis extensions were rejected on an existing rule.** Indicators,
trading calendars, performance metrics and backtest mechanics are PQT-owned,
because those libraries encode US market structure — no price bands, no
fractional-share prohibition, no T+2.5, no foreign-ownership limits — and every
one of those assumptions is silently wrong in Vietnam. The licence question
would arise too, but the market-model argument settles it first.

**Outright rejection was seriously considered** and declined on the four ideas.
Coverage introspection and the MCP tool-discovery pattern are genuinely good,
cost nothing to adopt as design, and would have had to be invented anyway.
Discarding a well-tested idea because its origin is inconveniently licensed
would be a poor trade, and reading source to learn from is not the same act as
depending on it.

## Trade-offs

- PQT forgoes 32 ready connectors and continues to write its own. For Vietnam
  that is unavoidable; for global data it is deferred cost, not avoided cost.
- Vietnamese macro series reachable through `imf` or `econdb` are not reachable
  today either. When Phase 12 or a macro factor wants them, PQT will either
  integrate those upstream sources directly or invoke the trigger.
- The out-of-process posture means any use is manual and operator-driven. There
  is no scheduled OpenBB ingestion and there will not be one under this ADR.
- Deferral has a cost if it is forgotten. The trigger is recorded in the
  roadmap's deferred table specifically so that revisiting is a decision rather
  than a rediscovery.
- Reading an AGPL codebase for design ideas is deliberate and is bounded: ideas
  and structure, never field definitions or copied code. The boundary is stated
  here so that a future contributor does not treat "we studied OpenBB" as
  permission to paste from it.

## Consequences

- OpenBB appears in this repository as documentation only. Any occurrence in
  `quant/pyproject.toml`, `docker-compose.yml`, `Directory.Packages.props`, or
  in any `.cs`, `.py`, `.ts`, `.tsx` or `.cpp` source file is a defect.
- Gate A is unaffected. U3 remains mandatory, remains Vietnamese, and gains
  only a scope note on provider capability metadata and explicit selection.
- The four adopted ideas are scope notes on U3, Phase 5 and Phase 18. **No new
  phase and no new workstream is created**; phase numbering stays frozen per
  the roadmap.
- The bar-level provenance gap is now a recorded prerequisite. Any future work
  that would make Tier 2 ingestion routine must answer it first.
- If the trigger fires, this ADR is superseded by a new one rather than edited,
  per the convention in [`README.md`](README.md).
- Research detail, the full gap analysis, the source-tier model and the official
  reference list are in [`../openbb-evaluation.md`](../openbb-evaluation.md).
