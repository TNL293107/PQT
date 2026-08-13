# ADR-007: Private, proprietary repository

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

This repository will eventually hold material that is not safely public:

- Trading strategies and factor definitions, whose value depends on not being
  widely known.
- Configuration shaped around specific broker and data provider integrations.
- Portfolio structure and risk limits.
- Personal financial context implied by all of the above.

It will also touch third-party data whose licences restrict redistribution
(see [`../data-policy.md`](../data-policy.md)).

A licence decision cannot be deferred. Source published without one is
implicitly all-rights-reserved but ambiguously so, and re-licensing later
requires the consent of every contributor.

## Decision

The repository is **private** and the source is **proprietary — all rights
reserved**. `LICENSE.md` states this explicitly.

No open-source licence (MIT, Apache-2.0, BSD, GPL, LGPL) is applied.

## Alternatives

**MIT or Apache-2.0, public.** Maximum portfolio visibility.

**Public repository, proprietary licence.** Readable, not reusable.

**Dual structure:** an open-source framework plus a private strategy repo.

## Reasoning

A permissive licence on a public repository is the standard choice for a
portfolio project, and it was rejected on the specific content this repository
will hold. Strategies lose value when public. Broker integration details and
risk configuration are a security matter, not merely a competitive one. And
personal financial information cannot be un-published once it has been indexed.

Public-but-proprietary keeps the portfolio benefit while removing reuse rights,
but it does not remove the disclosure problem: the code would still expose
strategy logic and account-shaped configuration to anyone who looks.

The dual structure is the correct long-term answer if the generic parts ever
become worth sharing — a backtesting engine and an instrument master are not
secret. It was rejected *for now* because splitting a repository that does not
yet contain either is premature, and the split is straightforward to perform
later precisely because the layering is enforced (ADR-001).

Private and proprietary is the reversible choice. Opening a repository later is
a decision; closing one that has been public is not.

## Trade-offs

- No public portfolio artefact. Mitigated by sharing access directly, or by
  extracting a sanitised subset later.
- No external contributions or review.
- No community scrutiny of security or correctness. Mitigated by CI, static
  analysis, warnings-as-errors, and the dependency vulnerability gate.
- GitHub Actions minutes on private repositories are limited rather than free.

## Consequences

- `LICENSE.md` carries a proprietary all-rights-reserved notice and states
  explicitly that the project is not open source.
- Third-party dependencies remain under their own licences; this notice does
  not purport to cover them.
- Market data is explicitly **not** covered by the repository licence and is
  governed by provider terms.
- The README must never present the project as open source.
- Repository visibility is not changed by tooling or automation. It is a
  deliberate manual act by the owner.
- If any part is ever extracted for publication, it needs its own ADR, its own
  licence, and a full secret-history audit first.
