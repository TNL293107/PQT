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
| [011](ADR-011-market-data-ingestion.md)                      | Market data ingestion, provenance and resume state | Accepted |
| [012](ADR-012-identifier-aliases-and-provider-import.md)     | Identifier aliases and the provider import pipeline | Accepted |
| [013](ADR-013-data-quality-and-lineage.md)                   | Data quality rules, the trading calendar and lineage | Accepted |
| [014](ADR-014-corporate-actions-and-adjusted-prices.md)      | Corporate actions and adjusted prices | Accepted |
| [015](ADR-015-vietnam-market-data-provider.md)               | Vietnamese market data provider integration | Accepted |
| [016](ADR-016-python-dotnet-research-boundary.md)            | The boundary between the backend and the quant layer | Accepted |
| [017](ADR-017-qlib-research-adapter.md)                      | Qlib as a research-only adapter | Accepted |
| [018](ADR-018-point-in-time-market-bars.md)                  | Point-in-time market bars       | Accepted |

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
