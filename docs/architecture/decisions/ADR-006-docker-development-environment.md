# ADR-006: Docker Compose for the development environment

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

The system already needs four processes to run: PostgreSQL, Redis, the backend
and the frontend. It is developed on Windows and will be built and tested on
Linux CI runners.

Installing PostgreSQL and Redis natively on Windows is possible but produces a
setup that differs from CI in version, configuration and filesystem behaviour —
the classic source of failures that reproduce in only one place.

## Decision

Docker Compose defines the development environment: `postgres`, `redis`,
`backend`, `frontend`.

```bash
docker compose up --build
```

Service dependencies use health conditions, not just start order. Both data
services have persistent named volumes. Every credential comes from `.env`,
which is git-ignored, and required values fail loudly when absent.

## Alternatives

**Native installation** of PostgreSQL and Redis.

**Compose for data services only**, running the backend and frontend on the
host.

**Dev Containers**, developing entirely inside a container.

**Kubernetes** (kind, minikube) to match a production topology.

## Reasoning

Compose gives one command, pinned versions identical to CI, and disposable
state. That last point matters more than it first appears: `docker compose down
-v` returning the database to empty is what makes migration testing honest.

Compose-for-data-only is in practice how most day-to-day work will happen —
running the backend under a debugger on the host against containerised
PostgreSQL and Redis. That workflow is fully supported: `appsettings.
Development.json` defaults to `localhost`, and the published ports make it
work. This ADR does not force everything into containers; it makes the full
containerised path *also* work, so "does it run from scratch?" has an answer.

Dev Containers were rejected as too heavy for a single developer with a working
local toolchain, and they interpose a layer between the debugger and the code.

Kubernetes was rejected outright, consistent with ADR-001. It solves
orchestration problems this system does not have, at a cost paid on every
single run.

Health conditions rather than plain `depends_on` are load-bearing: PostgreSQL's
entrypoint briefly starts a temporary server during initialisation, so a
container that is *running* is not necessarily a database that will accept
connections. Without `condition: service_healthy`, the backend races
initialisation and its first migration attempt fails intermittently.

## Trade-offs

- Docker must be installed and running; on Windows that means WSL 2 and a
  non-trivial amount of memory.
- Container startup adds time compared with native services.
- Compose is a development tool. It is not a deployment manifest and must not
  become one.
- Rebuilding the backend image on every change is slow, which is precisely why
  host-run development against containerised data services stays supported.

## Consequences

- `.env` is required. `docker compose up` fails immediately with a readable
  message if `POSTGRES_PASSWORD` is unset, rather than starting an
  unauthenticated database.
- Image tags are pinned to major versions (`postgres:17-alpine`,
  `redis:8-alpine`) and the integration tests start containers from the same
  tags, so tests never pass against a database the environment does not run.
- The backend image installs `curl` solely so Compose can probe `/health`; the
  ASP.NET runtime image ships no HTTP client.
- The backend applies migrations on start-up **in this environment only**, via
  an explicit configuration flag that defaults to `false` elsewhere.
- The frontend is compiled and served by nginx rather than running the Vite dev
  server in a container — closer to how it would be deployed, and it makes the
  production build path part of the routine.
- CI does not use this file. GitHub Actions service containers cover the same
  ground with less startup cost.
