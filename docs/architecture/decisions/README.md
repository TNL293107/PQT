# Architecture Decision Records

Short records of decisions that were expensive to make and would be expensive
to reverse. Each states the context, the decision, what was rejected, and what
it costs.

An ADR is not updated when a decision changes. A new ADR supersedes it, so the
reasoning at the time is preserved.

| ADR                                                          | Decision                        | Status   |
| ------------------------------------------------------------ | ------------------------------- | -------- |
| [001](ADR-001-modular-monolith.md)                           | Modular monolith backend        | Accepted |
| [002](ADR-002-postgresql.md)                                 | PostgreSQL as system of record  | Accepted |
| [003](ADR-003-redis.md)                                      | Redis for cache and realtime    | Accepted |
| [004](ADR-004-python-quant-layer.md)                         | Python for quantitative work    | Accepted |
| [005](ADR-005-cpp-performance-layer.md)                      | C++ for the latency-bound path  | Accepted |
| [006](ADR-006-docker-development-environment.md)             | Docker Compose for development  | Accepted |
| [007](ADR-007-private-proprietary-repository.md)             | Private, proprietary repository | Accepted |
| [008](ADR-008-vietnam-market-first.md)                       | Vietnam market first            | Accepted |
| [009](ADR-009-instrument-identity-and-ticker-lifecycle.md)   | Instrument identity and ticker lifecycle | Accepted |
| [010](ADR-010-instrument-search-and-security-context.md)     | Instrument search, resolution and current security | Accepted |

## Format

```markdown
# ADR-NNN: Title

Status | Date | Phase

## Context      what forced a decision
## Decision     what was decided
## Alternatives what else was considered
## Reasoning    why this one
## Trade-offs   what it costs
## Consequences what must now be true
```
