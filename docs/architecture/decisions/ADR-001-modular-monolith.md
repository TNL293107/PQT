# ADR-001: Modular monolith for the backend

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

The system will eventually span at least ten domains: instrument master,
market data, data quality, research, screening, quant, backtesting, portfolio,
risk, and execution. Several have genuinely different runtime profiles — a
realtime market data consumer looks nothing like a nightly backtest.

That list invites a service-per-domain design. But the operating conditions
are specific: one developer, one operator, one machine, and no domain
boundaries that have been validated by working code. At Phase 0 the domain
model does not exist at all.

## Decision

One deployable backend, internally separated into four projects:

```
PersonalQuant.Api             HTTP, middleware, composition
PersonalQuant.Application     use cases, ports
PersonalQuant.Domain          the financial model
PersonalQuant.Infrastructure  PostgreSQL, Redis, providers
```

Dependencies point inward and are enforced by project references, not by
review. `Domain` has no project reference at all, so it cannot reach a
database even by accident.

Domain modules will be added as folders and, if they grow, as further projects
inside the same solution — not as separately deployed services.

## Alternatives

**Microservices per domain.** Independent deployment and scaling; matches the
eventual domain map.

**Single project, no internal boundaries.** Fastest to start.

**Modular monolith with an in-process message bus.** Modules communicate only
through events, easing later extraction.

## Reasoning

Microservices trade local complexity for distributed complexity, and the
distributed kind has to be paid immediately and permanently: network failure
handling, distributed transactions, versioned contracts, correlated tracing,
and an environment that cannot be run from one command. None of that buys
anything for a single-operator system. Worse, service boundaries drawn before
the domain model exists are guesses, and a wrong boundary is far more
expensive to move across processes than across namespaces.

A single project with no boundaries is the opposite failure. This codebase is
expected to live for years and accumulate ten domains; without enforced
layering, infrastructure concerns leak into the model early and permanently.

The in-process bus is a reasonable idea prematurely applied. Event plumbing
between modules that do not exist yet is speculative generality, and it makes
every call path harder to follow for no current benefit. It can be introduced
later, for specific module pairs, once there is evidence the coupling is real.

The monolith keeps the whole system refactorable. Moving a boundary is a
rename; extracting a service later is a real but bounded project, and by then
the boundary will have been proven by use.

## Trade-offs

- Everything scales together. Acceptable: one operator, one machine.
- Nothing forces module isolation the way a network boundary does. Mitigated
  by project references and reviewed dependencies, but it does require
  discipline.
- A crash takes the whole backend down. Acceptable for personal research;
  revisited in Phase 17 if any component becomes latency- or uptime-critical.
- Everything is one language and runtime. The Python and C++ layers are
  deliberately outside this boundary for exactly that reason.

## Consequences

- One `docker compose up` starts the entire backend.
- Cross-module calls are direct method calls; no serialisation, no retries, no
  eventual consistency to reason about.
- The build fails on an inward-pointing dependency violation.
- Extraction remains available. Candidates, if they ever justify it: realtime
  market data ingestion (Phase 2) and the execution path (Phase 12).
- No Kubernetes, no service mesh, no message broker in Phase 0. Adding any of
  them requires a superseding ADR that names the specific problem it solves.
