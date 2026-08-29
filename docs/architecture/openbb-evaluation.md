# OpenBB Evaluation

**Status: RESEARCH RECORD.** Nothing here is implemented, scheduled, or
depended upon. This document records what OpenBB is, what it is not, which of
its ideas PQT takes, and which it declines. The decision it supports is
[ADR-019](decisions/ADR-019-openbb-boundary.md).

**Verified against official sources on 2026-08-29.** Every count and version
below is a snapshot of the revision reviewed on that date, not a durable fact.
Anything that could not be confirmed is marked *unverified* rather than
dropped.

Roadmap and workstream numbering: [`../roadmap/pqt-roadmap-v2.md`](../roadmap/pqt-roadmap-v2.md).

---

## Why this was investigated

A Facebook post described OpenBB as an open-source Bloomberg Terminal with
backtesting, LangChain and LlamaIndex integrations, an MCP server, a Python
SDK and multi-provider data, and asked whether PQT should adopt it.

**The post is the motivation for this research and is not a technical
authority.** Every claim below was checked against the OpenBB website, the
official documentation, or the repository itself, and the post's claims are
adjudicated in [their own section](#the-claims-that-prompted-this). Several are
wrong.

---

## What OpenBB actually is, as of 2026-08-29

**Not a terminal.** The product once called the OpenBB Terminal is not offered
on the OpenBB website today. The current line is two things:

| Product | What it is | Licence |
| --- | --- | --- |
| **ODP — Open Data Platform** | A Python data-connector toolkit: "connect once, consume everywhere". Ships as ODP Python, ODP CLI and ODP Desktop over one core | `AGPL-3.0-only` |
| **OpenBB Workspace** | A dashboard and AI-agent UI for investment teams, `pro.openbb.co`, with a free Community Edition and paid tiers | Commercial; **not in the AGPL repository** |

ODP positions itself as integration infrastructure — it "does not host or serve
any data, and it provides connectors without warranty or support". It is a way
of reaching other people's data, not a data source and not an analysis
platform.

The repository carries two release lines: `Open-Data-Platform-v1.0.2`
(2026-04-25) and the `openbb` meta-package at `4.7.3`, with `openbb-core` at
`1.6.13`. Default branch `develop`; last push observed 2026-07-30.

### Architecture

```
openbb_platform/
├── core/openbb_core/
│   ├── provider/
│   │   ├── abstract/     Provider · Fetcher · Data · QueryParams · AnnotatedResult
│   │   ├── registry.py   providers loaded from Python entry points
│   │   ├── query_executor.py
│   │   └── standard_models/   180 standardized Pydantic dataset shapes
│   ├── api/              rest_api:app  (FastAPI)
│   └── app/              routers, extension loader, package build
├── extensions/           18 packages
│   ├── routers (15)      commodity · crypto · currency · derivatives ·
│   │                     econometrics · economy · equity · etf · famafrench ·
│   │                     fixedincome · index · news · quantitative ·
│   │                     regulators · technical
│   └── interface (3)     mcp_server · platform_api · devtools
├── providers/            32 packages
└── obbject_extensions/   charting
```

Three abstractions carry the design:

| Type | Shape |
| --- | --- |
| `Provider` | `name`, `description`, `website`, `credentials`, `fetcher_dict` mapping a standard model name to a `Fetcher` |
| `Fetcher[Q, R]` | A three-stage ETL: `transform_query` → `extract_data` → `transform_data` |
| `AnnotatedResult` | `result` plus a free-form `metadata` dict, so a fetcher can return more than rows |

Providers register through Python entry points, discovered by `ExtensionLoader`
and collected in a `Registry`. The 32 provider packages observed were
`alpha_vantage`, `benzinga`, `biztoc`, `bls`, `cboe`, `cftc`, `congress_gov`,
`deribit`, `ecb`, `econdb`, `eia`, `famafrench`, `federal_reserve`, `finra`,
`finviz`, `fmp`, `fred`, `government_us`, `imf`, `intrinio`, `multpl`,
`nasdaq`, `oecd`, `sec`, `seeking_alpha`, `stockgrid`, `tiingo`, `tmx`,
`tradier`, `tradingeconomics`, `wsj` and `yfinance`.

### The four consumption surfaces

One core, reached four ways — this is OpenBB's best structural idea.

| Surface | How |
| --- | --- |
| **Python** | `from openbb import obb`; `obb.equity.price.historical(...)` returns an `OBBject` carrying `results`, `provider`, `warnings` and `chart`, with `.to_df()` |
| **REST** | `uvicorn openbb_core.api.rest_api:app` — FastAPI, endpoints generated from the routers, Swagger at `/docs` and ReDoc at `/redoc`, optional Basic auth via `OPENBB_API_AUTH` |
| **CLI** | `openbb-cli`, a wrapper over the installed ODP packages with interactive tables, charts and routine scripts |
| **MCP** | `openbb-mcp-server` wraps the FastAPI app; stdio, sse and streamable-http transports; tool categories derived from router paths; dynamic discovery through `available_categories`, `available_tools`, `activate_tools`, `deactivate_tools`; settings at `~/.openbb_platform/mcp_settings.json` |

Provider selection is a `provider=` argument. Omitted, the platform takes the
first available provider alphabetically and skips any whose credentials are
absent. Coverage is introspectable at runtime through `obb.coverage.providers`
and `obb.reference`.

**Workspace backends** are ordinary REST services that expose `/widgets.json`
(widget descriptors — name, category, endpoint, widget type, grid size,
parameters) and `/apps.json` (tab and layout composition), plus the data
endpoints those descriptors point at, with CORS and an `X-API-KEY` header.

---

## The claims that prompted this

| Claim | Verdict | Evidence |
| --- | --- | --- |
| Stocks, ETFs, crypto, macro, news, derivatives, forex | **Verified** | Router extensions `equity`, `etf`, `crypto`, `economy`, `news`, `derivatives`, `currency` |
| Technical analysis, quantitative analysis, econometrics | **Verified** | Extensions `technical`, `quantitative`, `econometrics` |
| Python SDK, REST API, MCP server | **Verified** | `from openbb import obb`; `openbb_core.api.rest_api:app`; `openbb-mcp-server` |
| Multiple providers | **Verified** | 32 provider packages |
| "Bloomberg Terminal mã nguồn mở" | **Outdated and misleading** | The Terminal is not a current product. ODP is a connector SDK; Workspace is a commercial UI outside the AGPL repository. Calling the pair an open-source Bloomberg misdescribes both halves |
| "terminal" | **Inaccurate as stated** | A CLI exists. A TUI terminal product does not |
| Cloud workspace | **Partly true** | Workspace exists, but as a commercial product, not as the open-source artefact the post implies |
| **Backtesting** | **False** | No backtesting engine, module or extension was identified. The string `backtest` occurs only in example notebooks, an SEC statement-schema note and recorded HTTP test fixtures |
| **LangChain integration** | **False** | No integration was identified. The only substantive occurrence is `examples/openbb_vs_langchain.ipynb` — a *comparison*, not an integration |
| **LlamaIndex integration** | **False** | Zero occurrences of `llama_index` or `llamaindex` were found in the repository |

**The two false claims are the ones that would have mattered most to PQT.**
Backtesting is Phase 8 and is the part of PQT least willing to inherit someone
else's market model; an agent framework would have been a dependency decision.
Neither is on offer.

---

## The two gaps that decide the matter

### 1. No Vietnamese equity market

Vietnam appears in OpenBB, but only at the **macro and country level** — in the
country lists and indicator codes of `imf`, `econdb`, `tradingeconomics`, `fmp`
and `bls`. That is real coverage and it is worth knowing: Vietnamese GDP, CPI
and similar national series are reachable.

**No Vietnamese equity-market provider was identified in the reviewed provider
catalogue on 2026-08-29** — no HOSE, HNX or UPCOM listings, no VN30 membership,
no Vietnamese corporate actions, fundamentals or ownership. The review covered
the provider directory and the official provider documentation page; it did not
enumerate every provider's full instrument universe, so this is a finding about
the catalogue rather than a proof of absence.

Searches for `HOSE` and `UPCOM` return matches, but inspection shows they are
tokenisation artefacts in unrelated files (IPO calendars, README prose) and not
Vietnamese venue references.

**Consequence: OpenBB cannot serve U3 or Gate A.** The one piece of work on
PQT's critical path is exactly the piece OpenBB does not address.

### 2. No point-in-time model

`point_in_time` returns zero occurrences repository-wide. `as_of` appears in
seven places, all provider-specific field names on unrelated models
(`nport_disclosure`, `etf_holdings`, `sec_filing`, `calendar_earnings`,
`central_bank_holdings` and two Federal Reserve helpers).

**No point-in-time, revision, observation-time, dataset-versioning or
manifest-hashing model was identified in the reviewed repository and
documentation on 2026-08-29** — that is, none of U1, U4 or U5.

This is not a criticism. OpenBB is a connector layer and a connector layer has
no business inventing a temporal model. It does mean that no OpenBB output
could enter a PQT canonical dataset without passing PQT's own normalisation,
validation, point-in-time and provenance stages first — which is the same thing
PQT requires of every other source, and therefore not an extra cost.

### And a third consideration: licence

OpenBB is `AGPL-3.0-only`. This repository is proprietary, all rights reserved
([ADR-007](decisions/ADR-007-private-proprietary-repository.md)).

[ADR-017](decisions/ADR-017-qlib-research-adapter.md) already treats licence as
a decision input, and recorded Qlib's MIT terms as imposing no copyleft
obligation. The same input applied to OpenBB points the other way. That is
enough to keep OpenBB out of the PQT process without anyone needing to reach a
legal conclusion, and this document reaches none: it states the licences as
facts and the engineering consequence that follows, and stops there. Whether
any particular arrangement would be permitted is a question for the operator,
not for an architecture document.

---

## Gap analysis

Classified `ADOPT` · `ADAPT` · `REFERENCE ONLY` · `REJECT`. The test applied to
each row was not *does OpenBB have this* but **does this materially improve
PQT as a quantitative research and market intelligence terminal**.

| OpenBB capability | What OpenBB actually provides | PQT equivalent today | Gap | Action | Lands in |
| --- | --- | --- | --- | --- | --- |
| Unified layer over providers | Router → fetcher → standard model | `IMarketDataProvider` → raw batch → normalise → canonical bars → PIT → adjust → universe → dataset | None. PQT's pipeline **is** the gateway, and carries more | `REFERENCE ONLY` — a separate "Data Gateway" component would duplicate `MarketDataFetcher`, `MarketDataNormalizer` and `MarketDataIngestionService` | no task |
| Provider capability metadata | `Provider(name, credentials, fetcher_dict)`; `obb.coverage.providers` | `Code` and `SupportedIntervals` only | Coverage and health are not declared | `ADAPT` | U3 |
| Provider selection | `provider=`; falls through to the next provider when credentials are missing | `TryResolveDefault` fails outright once more than one provider is registered | Explicit selection is missing | `ADAPT` the selection | U3 |
| Automatic provider fallback | Silent fall-through at selection time | none | — | **`REJECT`.** A series silently assembled from two providers would carry mixed semantics into a backtest. PQT does explicit selection, records `source` per bar, and raises a Phase 3 finding when a series mixes sources | U3 |
| Coverage introspection | `obb.coverage.providers`, `obb.reference.keys()` | none | Cannot answer "what data does PQT hold for FPT" | `ADOPT` — Dataset & Coverage Registry | Phase 5 |
| Standardized data models | 180 Pydantic shapes | PQT canonical schema | PQT's schema is narrower but carries lineage OpenBB's does not | `REFERENCE ONLY` — read the catalogue as a checklist of dataset shapes worth having. **Do not copy field definitions**; the source is AGPL | Phase 6 · 11 |
| The provider ecosystem itself | 32 connectors, mostly US and global | Vietnam only, and unvalidated | No Vietnamese equities; Vietnamese macro is reachable | `DEFERRED` with a recorded trigger — optional, out-of-process, operator-installed Tier 2 collector | [ADR-019](decisions/ADR-019-openbb-boundary.md) |
| One core, many interfaces | Python · REST · CLI · MCP over one router set | `Api → Application → Domain` exists; no CLI, SDK or MCP | The rule is right and PQT half-holds it | `ADOPT` as a rule: **no interface may hold business logic** | architectural principle |
| Python SDK | `obb.equity.price.historical(...)` → `OBBject` → `.to_df()` | — | — | `ADAPT` — **already owned by U6 and U10.** Named as a deliverable there, not as a new task | U6 · U10 |
| CLI | `openbb-cli` over the same core | none; ingestion is host-driven and there is no HTTP write surface | An operator has no command surface | `ADAPT` — the natural surface for the bulk-load path U3 already owns | U3 |
| MCP server | FastAPI app wrapped; category-based dynamic tool discovery | none | — | `ADOPT` — MCP as the AI transport, with progressive tool activation | Phase 18 |
| PIT-aware, provenance-carrying tools | **Not an OpenBB capability** | none | — | **PQT-specific.** Every tool takes `knownAsOf` and returns provenance | Phase 18 |
| Quant Tool Registry | **Not an OpenBB capability** | none | — | **PQT-specific.** The AI reaches data through registered tools only, never arbitrary SQL | Phase 18 |
| `widgets.json` / `apps.json` | Declarative widget and app descriptors | Phase 5 workspace, planned | — | `REFERENCE ONLY` — the declarative-descriptor idea, not the format | Phase 5 |
| PQT as a Workspace backend | Any REST service can be one | — | — | **`REJECT`.** It would make PQT a data plugin inside someone else's UI, which is the opposite of the Bloomberg-inspired terminal PQT is for | — |
| `technical` · `quantitative` · `econometrics` | Indicator and statistics toolkits | PQT-owned, Phase 7 | — | **`REJECT`.** AGPL, and the existing rule stands: libraries of financial semantics smuggle in US market structure | — |
| Backtesting | **None** | Phase 8 | — | Nothing to take. Recorded so the claim is not repeated | — |
| LangChain · LlamaIndex | **Not integrations** | Phase 18 | — | **`REJECT`.** Not an OpenBB capability, and MCP is already the interface. A framework dependency with no capability behind it | — |
| Research dataset building | — | **Already owned**: U5 manifest, U6 `FeatureEngine`/`FactorEngine`, U9 run record | The only real delta is a declarative spec with a forward-return label | `ADAPT` — extend, **do not create a duplicate task** | Phase 7 |

### What OpenBB leaves behind, once filtered

```
OpenBB
 │
 ├── Data  → PQT     ✗  not taken (no Vietnamese equities; Gate A unaffected)
 ├── Code  → PQT     ✗  not taken (AGPL; no dependency, no import, no container)
 │
 └── Architecture    ✓
       ├── Provider capability / coverage metadata  →  U3
       ├── Coverage introspection                   →  Phase 5
       ├── One core, many interfaces                →  architectural rule
       └── MCP tool discovery                       →  Phase 18
```

Four ideas and one deferred option. That is the whole of it.

---

## Source tiers

PQT reaches data at three tiers. The distinction is about **ownership and
obligation**, not quality.

| Tier | What | Examples | Status |
| --- | --- | --- | --- |
| **Tier 1 — PQT Native** | Vietnamese market data PQT ingests directly and treats as its subject | HOSE · HNX · UPCOM · VN30 · Vietnamese fundamentals, corporate actions, ownership, news | The project. U3 delivers the first real one |
| **Tier 2 — External** | Non-Vietnamese or supplementary data reached through an optional, out-of-process collector | US and global equities, ETFs, options, futures, FX, crypto, global macro and news — OpenBB is one possible route | `DEFERRED`. [ADR-019](decisions/ADR-019-openbb-boundary.md) |
| **Tier 3 — User / private** | Data the operator supplies | CSV exports, broker statements, proprietary factors, private research sets | The file-ingestion seam already serves this |

### Access broadly, canonicalize selectively

The governing rule, and the reason breadth is not a threat:

> Being able to *reach* a dataset costs nothing. Making it **canonical** costs
> a domain model, a quality rule set, a provenance story and a maintenance
> obligation forever.

**No Tier 2 or Tier 3 data becomes canonical without passing the same stages
Tier 1 data passes** — normalisation, validation, provenance, point-in-time,
adjustment. There is no shortcut into `quant.*` and no second pipeline.

This is what lets PQT contemplate a broad external ecosystem without inheriting
hundreds of bespoke domain models: most external data is *consulted*, and only
the little that earns a place is *canonicalized*.

---

## Resulting architecture

Unchanged from what PQT already builds. OpenBB, if it is ever used, is one
optional box on the left and touches nothing else.

```
   Tier 1 — PQT Native      Tier 2 — External          Tier 3 — User
   Vietnamese providers     optional, out-of-process   CSV · broker exports
                            (OpenBB is one option)
         │                          │                        │
         └──────────────────────────┼────────────────────────┘
                                    ▼
                    Provider adapter / file-ingestion seam
                    ← provider names, units, symbology stop HERE
                                    ▼
                    RAW batch → normalisation → canonical bars
                                    ▼
              PIT (U1) → announcement filter (U4) → universe (U2)
                                    ▼
              Canonical dataset — Parquet + hashed manifest (U5)
                                    ▼
         ┌──────────────────────────┼──────────────────────────┐
      Research                  Backtesting                  Risk
         └──────────────────────────┼──────────────────────────┘
                                    ▼
                Application layer — one implementation
                                    ▼
            REST · Python SDK · CLI · MCP   (interfaces only)
                                    ▼
                           AI Quant Analyst
```

### The AI path, stated separately because it is a boundary

```
AI agent
   ↓
MCP server                    transport only
   ↓
Quant Tool Registry           declared tools, declared schemas
   ↓
PQT application layer         the same one REST, Python and the CLI use
   ↓
Data · Research · Backtest · Risk
```

```
AI  →  OMS / live execution  =  NOT ALLOWED
```

The AI has no path to the order management system, and no tool may create one.
This is the same constraint the roadmap already states for Phase 18; MCP does
not relax it, and the tool registry is what makes it enforceable rather than
merely stated — an agent that can only call registered tools cannot reach an
order path that has no tool.

---

## What this document does not claim

- It does not claim OpenBB has been installed, run, or tested. It has not.
- It does not claim OpenBB has been evaluated against its own licence terms or
  against the terms of the data providers it connects to, and it takes **no
  position** on whether any particular use would be permitted.
- It does not claim the provider catalogue was exhaustively searched for
  Vietnamese instruments. The catalogue and its documentation were reviewed;
  individual provider universes were not enumerated.
- It does not claim any of the four adopted ideas is scheduled. Each is a scope
  note on an existing phase or workstream, and none changes Gate A.

---

## Official references

Consulted on 2026-08-29. The Facebook post that motivated this research is
deliberately not cited as a technical source.

| Source | URL |
| --- | --- |
| OpenBB website | https://openbb.co/ |
| GitHub repository | https://github.com/OpenBB-finance/OpenBB |
| Documentation root | https://docs.openbb.co/ |
| Open Data Platform | https://docs.openbb.co/odp |
| ODP Python | https://docs.openbb.co/odp/python |
| Python quickstart | https://docs.openbb.co/odp/python/quickstart |
| REST API | https://docs.openbb.co/odp/python/quickstart/rest_api |
| Extensions | https://docs.openbb.co/odp/python/extensions |
| Providers | https://docs.openbb.co/odp/python/extensions/providers |
| MCP server extension | https://docs.openbb.co/odp/python/extensions/interface/openbb-mcp |
| MCP settings | https://docs.openbb.co/odp/python/settings/mcp_settings |
| ODP CLI | https://docs.openbb.co/odp/cli |
| Workspace data integration | https://docs.openbb.co/workspace/developers/data-integration |
| Workspace MCP tools | https://docs.openbb.co/workspace/developers/ai-features/mcp-tools |

Repository facts were read directly from `openbb_platform/` on the `develop`
branch: `LICENSE`, `openbb_platform/pyproject.toml`,
`openbb_platform/core/pyproject.toml`,
`openbb_platform/core/openbb_core/provider/abstract/`,
`.../provider/registry.py`, `.../provider/standard_models/`,
`openbb_platform/extensions/` and `openbb_platform/providers/`.
