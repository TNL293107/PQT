# ADR-003: Redis for cache, ephemeral state and realtime fan-out

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

Several planned capabilities need state that is hot, short-lived, and not
worth a durable write:

- Latest quote per instrument, read on every screen refresh.
- Realtime session state for a streaming market data path (Phase 2).
- Fan-out of tick updates to connected terminal clients.
- Rate limiting against provider API quotas — a hard requirement, since
  exceeding a quota can suspend an account.
- Expensive computed results, such as screener and factor runs (Phase 6).

Writing all of that to PostgreSQL would mean durable writes for data whose
value expires in seconds, and polling where push is wanted.

## Decision

Redis 8, running in Docker, reached through `StackExchange.Redis`.

The connection is created lazily behind an `IRedisConnectionProvider`
abstraction, configured with `AbortOnConnectFail = false`.

Phase 0 caches nothing. Only the connection and its health check exist.

## Alternatives

**PostgreSQL for everything**, using `UNLOGGED` tables and `LISTEN/NOTIFY`.

**In-process memory cache** (`IMemoryCache`).

**A message broker** (RabbitMQ, Kafka) for the realtime path.

## Reasoning

PostgreSQL can do this, and for a single operator `LISTEN/NOTIFY` would even
work for fan-out. It was rejected because it conflates two responsibilities:
the system of record should not also be the hot path, and its failure modes
should not be coupled to a cache that is allowed to be unavailable. Redis is
allowed to be down. The database is not.

An in-process cache is faster and simpler, but it dies with the process and
cannot be shared between the backend and the future Python and ingestion
processes. It remains the right tool for genuinely process-local memoisation
and is not excluded by this decision.

A message broker is the correct answer for durable, ordered, replayable
streams — and this system has none of those requirements. Kafka in particular
would add a substantial operational burden for one user on one machine. Redis
pub/sub is fire-and-forget, which is exactly right for "latest price to a
connected screen", where a missed message is superseded a moment later anyway.
If durable event replay ever becomes a requirement, that warrants its own ADR.

`AbortOnConnectFail = false` and lazy connection are load-bearing: an
unavailable cache must degrade readiness, never prevent the API from starting.
Connecting eagerly during container build would make Redis a hard start-up
dependency, which inverts the intended failure model.

## Trade-offs

- A second data store to run, back up (or deliberately not), and reason about.
- Redis persistence is weaker than PostgreSQL's. Accepted: nothing that matters
  is stored only in Redis, ever.
- Pub/sub has no delivery guarantee and no replay. Accepted for realtime
  display; unacceptable for order events, which is why the OMS (Phase 13) will
  write to PostgreSQL.
- Cache invalidation becomes a real design concern from Phase 2.

## Consequences

- Readiness reports Redis separately from PostgreSQL, so a cache outage is
  visible and attributable.
- No durable state may live only in Redis. Anything Redis holds must be
  reconstructible from PostgreSQL or from a provider.
- The password is supported through configuration and left unset locally, where
  Redis is not reachable outside the Compose network.
- Revisit if a durable, replayable event log becomes a requirement — most
  likely alongside the OMS in Phase 13.
