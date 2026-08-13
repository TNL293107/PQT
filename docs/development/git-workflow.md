# Git Workflow

## Branching model

A trimmed GitHub Flow with a long-lived integration branch. It fits a single
developer working through a long, phased roadmap: `main` always describes a
state that works, while a phase in progress has somewhere to live.

```
main                 ●───────────────●───────────────●
                     │               ▲               ▲
                     │               │ merge         │ merge
                     │               │ (phase done)  │
develop              ●──●──●──●──●───●──●──●──●──●───●
                        ▲     ▲         ▲     ▲
                        │     │         │     │
feature/phase-1-…       ●──●──●         │     │
feature/phase-2-…                       ●──●──●
```

| Branch      | Purpose                                                     | Lifetime  |
| ----------- | ----------------------------------------------------------- | --------- |
| `main`      | Known-good. Every commit builds and passes CI.               | permanent |
| `develop`   | Integration. Where a phase is assembled.                     | permanent |
| `feature/*` | One unit of work — usually one phase, or one slice of one.   | temporary |
| `fix/*`     | Bug fix.                                                     | temporary |
| `chore/*`   | Tooling, dependencies, configuration.                        | temporary |
| `docs/*`    | Documentation only.                                          | temporary |

Existing branches are created on demand. Eighteen empty phase branches created
in advance would go stale, diverge from `develop`, and say nothing useful — a
branch is created when work on it starts.

## Naming

```
feature/phase-<n>-<short-slug>
fix/<short-slug>
chore/<short-slug>
docs/<short-slug>
```

Examples:

```
feature/phase-1-instrument-master
feature/phase-2-market-data-ingestion
fix/readiness-timeout-on-slow-postgres
chore/bump-efcore-10-0-12
docs/adr-008-timescaledb
```

Lower case, hyphen separated, no personal prefixes. The phase number ties the
branch to [the roadmap](../roadmap/phases.md).

## Starting work

```bash
git switch develop && git pull && git switch -c feature/phase-1-instrument-master
```

## Finishing work

Rebase onto `develop` to keep history linear, then merge:

```bash
git switch develop && git pull && git switch - && git rebase develop
```

```bash
git switch develop && git merge --no-ff feature/phase-1-instrument-master
```

`--no-ff` keeps the phase visible as a unit in the history.

`develop` merges into `main` when a phase is complete: builds, tests pass,
documentation updated, and the phase marked COMPLETE in the roadmap.

## Commit messages

Conventional Commits:

```
<type>: <description>

<optional body explaining why, not what>
```

| Type       | Use                                              |
| ---------- | ------------------------------------------------ |
| `feat`     | New capability                                   |
| `fix`      | Bug fix                                          |
| `refactor` | Behaviour-preserving restructuring               |
| `perf`     | Performance improvement                          |
| `docs`     | Documentation                                    |
| `test`     | Tests only                                       |
| `build`    | Build system, dependencies                       |
| `ci`       | CI configuration                                 |
| `chore`    | Everything else                                  |

Rules:

- Imperative mood: "add", not "added" or "adds".
- Lower case after the colon, no trailing full stop.
- Subject under 72 characters.
- The body explains *why*. The diff already shows what.

Good:

```
feat: resolve provider symbols to canonical instrument ids

Provider feeds disagree on symbology: AAPL, AAPL.US and US0378331005 all
denote the same security. Storing the provider symbol as the key would make
every later join between prices, fundamentals and positions unreliable.
```

Bad:

```
updated stuff
fix
WIP
```

## Commit size

One commit, one reason to change. A commit that touches the backend, the
frontend and CI is three commits.

This matters more than usual here: the repository is the record of how the
system was built. A history of large undifferentiated commits cannot be
bisected, reviewed, or reverted cleanly.

## What must never be committed

- `.env` or any populated environment file
- API keys, tokens, passwords, private keys, certificates
- Connection strings containing credentials
- Broker or provider credentials
- Vendor market data — see [data policy](../architecture/data-policy.md)
- Build output, `node_modules/`, virtual environments

`.gitignore` covers all of these. Verify before committing:

```bash
git status --short && git diff --cached
```

## If a secret is committed

1. **Do not just delete it in a new commit.** It stays in history.
2. Rotate the credential immediately. Assume it is compromised.
3. Only then decide whether to rewrite history.
4. If the commit was pushed, rotation is not optional.

History is never rewritten silently. Rewriting a pushed branch is a deliberate,
announced act.

## Line endings

`.gitattributes` normalises everything to LF in the repository, with CRLF
checked out for `.bat`, `.cmd` and `.ps1`. Do not set `core.autocrlf`
per-repository; the attributes file is the single source of truth, and it is
what keeps Windows development consistent with Linux CI and containers.
