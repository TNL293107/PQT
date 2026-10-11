# ADR-027: The first write path from a browser — an operator key

**Status:** Accepted · **Date:** 2026-10-11 · **Phase:** 5 — Market Intelligence Terminal

## Context

[ADR-026](ADR-026-market-intelligence-terminal.md) left the watchlist and the
workspace waiting for this decision. Both are state the operator creates, and
the API has never accepted a write: everything that changes the database
arrives through the CLI or a hosted service, which run with the database's own
credentials on the operator's machine. Authentication is Phase 19's.

The deployment publishes the API on every interface of the host
(`"${API_PORT:-8080}:8080"`). A write endpoint with nothing in front of it is
writable by anything that can reach that port: another device on the network,
or a page in the operator's own browser posting to `localhost:8080`. CORS limits
which origins may *read* a response; it does not stop a simple request from
being *sent*.

Two shortcuts are tempting and wrong. Checking for a loopback client address
fails under Docker, where every request arrives from the bridge gateway. Keeping
watchlists in browser storage avoids the question by making them invisible to
the CLI, the Python layer and a second browser, which ADR-026 rejected.

## Decision

**Every non-GET endpoint requires an operator key until Phase 19 replaces it
with authentication.**

- The key is configuration — `Terminal:OperatorKey`, set from `.env` as
  `PQT_OPERATOR_KEY` — at least 32 characters, validated at startup when set.
- A write carries it as `Authorization: Bearer <key>`. It is compared in
  constant time. Missing or wrong → `401`, with no hint which.
- **No key configured means writes are disabled**, answered `403` with a
  problem stating that the deployment has no operator key. Reads are unaffected.
  A fresh clone is therefore read-only until the operator opts in.
- Write endpoints accept `application/json` only. A cross-site form post cannot
  send a custom `Authorization` header, and a JSON request with one is
  preflighted, which CORS then refuses for a foreign origin.
- The console asks for the key once per tab (`key` verb) and holds it in
  `sessionStorage`, never in `localStorage`, a cookie or the URL.
- The CLI does not need the key: it writes through the application layer with
  database credentials, as every operator command already does.

**Operator state lives in its own schema, `terminal`.** Watchlists and
workspaces are not market facts, so they stay out of `quant`, the system of
record. They are not research output either, so they stay out of `research`,
whose grants and parity test belong to U10. `quant_research` gets no grant on
`terminal`.

## Alternatives

- **Authentication now.** It is Phase 19 and pulls in identity, sessions and
  user models for a single operator. Rejected as out of order.
- **Loopback-only binding of the published port.** It is still worth doing and
  is left to the operator's `.env`. It is no substitute for the key, because it
  does nothing about a hostile page in the operator's own browser.
- **A cookie session.** It brings CSRF back into scope and needs a login
  endpoint. A bearer key held per tab has neither problem.

## Reasoning

The smallest mechanism that makes "who may write" a deliberate choice rather
than an accident of network reach, and that Phase 19 can delete without
migrating data: the key guards endpoints, not rows, so nothing stored refers
to it.

## Trade-offs

- One shared credential. Anyone holding it is the operator, and that is exactly
  the single-user model PQT has until Phase 19.
- The operator pastes the key once per browser tab.

## Consequences

- An authorisation filter applies to every non-GET route group. A test asserts
  that no write endpoint is reachable without it, so a later endpoint cannot
  forget it.
- The watchlist (ADR-026 slice 4) is the first write endpoint; the workspace
  (slice 5) follows on the same path.
- `.env.example` documents `PQT_OPERATOR_KEY` and how to generate one.
