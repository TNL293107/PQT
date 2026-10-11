# ADR-026: The Market Intelligence Terminal's scope and first slices

**Status:** Accepted · **Date:** 2026-10-11 · **Phase:** 5 — Market Intelligence Terminal

## Context

Gates A and B passed on 11 October 2026. Phase 5 is next, and the roadmap gives
it one line: *command bar, charts, watchlist, market breadth, workspace, dataset
& coverage registry*. Some of that already exists. The console reads
`<security> <function> [options]`, resolves the security against the instrument
master, and answers `DES`, `GP`, `QLTY` and `ING`. `GP` draws a sparkline.

Phase 5 is the first phase whose purpose is a surface rather than a fact, which
is when business logic starts drifting into the interface that happens to need
it first. A breadth count computed in React is a breadth count the CLI, the
Python layer and Phase 18's tool registry each have to compute again, and four
implementations of "advancing" will not agree on what to do with an ex-date.

The six items also differ in kind. Four read facts PQT already records. Two —
the watchlist and the workspace — are state the operator creates, and the API
has no write endpoint at all: every write so far reaches the database through
the CLI or a hosted service, because nothing authenticates a browser.

## Decision

**Phase 5 is read-only over facts the data phases already record, and every
terminal function is an application-layer query reachable from the API, the
CLI and the console alike.** No function computes in the browser what the
server could compute, and no function exists in one interface only.

### The slices, in order

| # | Slice | Reads | Interfaces |
| --- | --- | --- | --- |
| 1 | **Coverage registry** | bars, ingestion journal, findings, corporate actions, memberships, calendar | `GET /instruments/{id}/coverage`, `pqt coverage`, console `COV` |
| 2 | **Market breadth** | point-in-time membership, adjusted closes, calendar | `GET /universes`, `GET /universes/{code}/breadth`, `pqt universe`, console `BREADTH` |
| 3 | **Price chart** | the existing bar series | console `GP` — presentation only, over the existing endpoint |
| 4 | Watchlist | operator state | after the write-path ADR — [ADR-027](ADR-027-operator-write-path.md) |
| 5 | Workspace | operator state | after the write-path ADR — [ADR-027](ADR-027-operator-write-path.md) |

### The honesty rule, applied to coverage

U2 established that an absent dataset and an unmeasured one must never render
identically. The registry gives every dataset one of four states and nothing
else:

| State | Meaning |
| --- | --- |
| `Held` | PQT holds it; the extent is stated |
| `NotHeld` | PQT collects this kind of data and holds none for this instrument |
| `NotMeasured` | Whether PQT should hold it cannot be said — a universe whose coverage was never declared, a span the calendar does not reach |
| `NotCollected` | PQT does not collect this kind of data yet — fundamentals until Phase 6 |

Missing sessions are counted only where the venue's calendar covers the span;
elsewhere the count is absent rather than zero.

### Breadth, defined once

For a universe and a session: the members are those whose spell contains the
session, as of that day. A member **advances** or **declines** by its close
against its previous session's close, on prices **adjusted for actions going ex
that day** — an ex-dividend gap is not a decline. A member without a bar on
either session is counted as **no data**, never as unchanged. A session the
venue calendar does not cover, or that is not a trading day, is refused rather
than answered.

### What waits

The watchlist and the workspace need the first write path from a browser. That
is decided in its own ADR before either is built, not slipped in beside a read
endpoint. Until then the API stays read-only.

## Alternatives

- **Compute breadth and coverage in the frontend from existing endpoints.** One
  request per member per session, the adjustment rule reimplemented in
  TypeScript, and nothing for the CLI or Phase 18. Rejected by the rule above.
- **Hold watchlists in browser storage.** Fast, and a watchlist the CLI, the
  Python layer and a second browser cannot see. Rejected for the same reason.
- **A charting library.** `GP` renders in the console's transcript and is
  tested under jsdom. Canvas libraries do not render there, and the chart needs
  a few hundred points, not a hundred thousand. An SVG chart written here is
  testable and adds no dependency; this is revisited if a function needs
  intraday depth.

## Reasoning

The rule is ADR-019's — one application core, many interfaces — applied at the
moment it is first under pressure. Ordering the read slices first delivers most
of the phase without deciding the write path under schedule pressure.

## Trade-offs

- The terminal cannot yet save a watchlist. That is deliberate and visible.
- Breadth on adjusted closes differs from what a broker's board shows on an
  ex-date, and is right: the board's red is the dividend.

## Consequences

- Coverage, breadth and every later function live in `PersonalQuant.Application`
  with tests there; the API, CLI and console only parse, call and render.
- The four coverage states are an enum in the contract; no surface may render
  `NotMeasured` as `NotHeld`.
- The write-path ADR is the gate for slices 4 and 5.
