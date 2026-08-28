# ADR-015: Vietnamese market data provider integration

**Status:** Accepted · **Date:** 2026-08-28 · **Phase:** Research Foundation Upgrade (U3)

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
- **Open:** which provider is selected. The matrix decides it, and this ADR is updated with the outcome and the verified rows. A change of provider after selection is a new ADR, not an edit to this one.
