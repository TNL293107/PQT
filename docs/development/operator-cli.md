# Operator CLI

`PersonalQuant.Cli` is the command surface over the application layer. It exists
because ingestion and backfill were otherwise reachable only from a timer inside
the API host, which meant a one-off ingest had to be expressed as a
configuration change and a restart.

**It holds no business logic.** Every command parses arguments, calls one
existing application service and renders the result. A run it produces is
indistinguishable from one the scheduled host produces, because it is the same
run through the same pipeline into the same audit table.

---

## Running it

Inside the deployment, which is where it belongs:

```bash
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll provider list
```

The container's environment is the deployment's environment, so a command run
this way cannot reach a different database, or run under a different retry and
call-spacing policy, than the host it is operating.

On the host, for a database reachable from there:

```bash
dotnet run --project backend/src/PersonalQuant.Cli -- provider list
```

Configuration comes from `appsettings.json` beside the assembly and then from
the environment — the same variables the API host reads, documented in
`.env.example`. A command that needs configuration the environment did not
supply says which settings are missing and stops; it does not guess.

Nothing about the CLI starts the API's hosted services. It never migrates, never
seeds and never begins a scheduled pass.

---

## Commands

### `provider`

```bash
pqt provider list
pqt provider show <CODE>
pqt provider check <CODE> --instrument <TICKER> [--interval 1d] [--from yyyy-MM-dd]
```

`show` renders every declared field, including two that decide what a series
*is* rather than how convenient it is to fetch: whether the source already
adjusted its prices for corporate actions, and which trades its volume counts.
The second matters because Vietnamese venues run two books — continuous
matching, and negotiated blocks agreed off it — and a volume that counts only
the first understates traded size by a margin that varies day to day. A
liquidity screen, a participation-rate cap and an execution-cost model each mean
something different depending on that value, and none of them can detect which.
A source that has not stated a basis renders as `unknown`, never as "all".

`list` and `show` read what each registered source **declares** — no call is
made to any third party, so they are safe to run against a metered source, and
they work on a host that cannot reach PostgreSQL. That last part is deliberate:
asking a host what sources it thinks it has is most useful when it will not
start.

A value the source did not state renders as `unknown`, never as unbounded and
never as blank. An empty venue set renders as `any`, because "no restriction" is
a claim a directory of CSV files genuinely makes and is not the same as a vendor
that never said.

`check` asks the registry the same question the ingestion pipeline asks, with
the same criteria, and prints the outcome — `Selected`, `Incapable`,
`Ambiguous`, `Unknown` or `None` — with the reason naming the dimension that
failed. With `--from` it also says whether the requested start would be clamped
forward to a declared coverage floor.

### `ingest`

```bash
pqt ingest run      --instrument <TICKER> [--interval 1d] [--source <CODE>]
                    [--from yyyy-MM-dd] [--to yyyy-MM-dd]

pqt ingest backfill --instrument <TICKER> --from yyyy-MM-dd [--to yyyy-MM-dd]
                    [--interval 1d] [--source <CODE>] [--max-passes 200]

pqt ingest backfill --universe <CODE> --from yyyy-MM-dd [--as-of yyyy-MM-dd] ...
```

`run` is one pass. With no `--from` it resumes from the checkpoint and stops at
the last period that has finished, which is what the scheduled pass does.

`backfill` is a loop over `run`, not a second pipeline. The service already
truncates a range longer than one call may carry and advances the checkpoint to
the newest bar actually stored, so repetition is all a backfill is. Only the
first pass names a start; every pass after it leaves the range open so the
checkpoint decides. The loop stops when a pass asks for the same range as the
one before it — the honest signal that the source has nothing further — and
`--max-passes` is a second stop for the case nobody predicted.

**Name `--source` whenever two registered sources could serve the request.**
There is no fallback and no priority order: ambiguity is refused, and the run
records both candidates rather than picking by registration order.

A universe backfill reads membership **as of the day the range starts**, not
today. Backfilling today's constituents over an earlier decade is the
survivorship bias the universe model exists to remove. Where that membership is
not known, the command refuses and names the reason — an unknown membership is
not an empty one, and backfilling it would produce a universe that looks sourced
and is not.

### `quality`

```bash
pqt quality list    --instrument <TICKER> [--interval 1d] [--limit 50]
pqt quality resolve <ID> --explained|--dismissed --reason "<text>"
```

`resolve` is the half that was missing. A finding stays open until something
accounts for it and the consistency score decays while it does, but the only
caller able to close one was the corporate-action path matching a price-limit
breach. Anything a person had to investigate stayed open with nowhere to close
it.

**Explained and dismissed are opposite claims and neither is a default.**
Explained says the discontinuity was real and something accounts for it;
dismissed says there was nothing there. Read back in five years the difference
is the whole value of the record, so the command refuses to proceed without one
of them and without a reason.

A finding that is already closed cannot be closed again. Overwriting the first
resolution would erase the audit trail the finding exists to leave.

### `schema` and `calendar`

```bash
pqt schema status
pqt calendar status
```

Both answer questions about degradations that are **correct and silent**, which
is why they need asking rather than waiting to be told.

`schema status` compares what the database has applied against what this build
carries, and prints the build's own informational version beside it. A pending
list says the database is behind the build; the version says whether the build
is behind the source. Those drifted independently once — an image two weeks old
against a database nine migrations older still, each internally consistent, the
API answering every request and the health check green. It exits non-zero when
the database is behind, and distinguishes a database that was never migrated
from one that stopped being maintained, because the remedies differ.

The API host now says the same thing at start-up. Not applying migrations is a
deployment policy; not knowing whether the database is behind is a defect, so
the schema is inspected either way and a gap is logged at warning naming every
missing migration.

`calendar status` prints how far each venue's recorded calendar reaches, how
many days remain, and its state:

| State | Meaning |
| --- | --- |
| `covered` | The declared claim reaches past today with more than a quarter to spare |
| `expiring` | The claim ends within 90 days |
| `lapsed` | The claim ended before today |
| `not declared` | Nobody has said how far this venue's calendar was transcribed |

Completeness is measured against this calendar. Past the date a venue is
covered through, a real holiday and a missing session become
indistinguishable, so completeness is reported as unknown rather than computed
wrongly — correct, and completely silent. The command exits non-zero once a
calendar has lapsed and warns while it is still expiring, because Vietnam's next
year cannot be derived: Tet is lunar and substitute days are set by annual
decree, so coverage exists only once somebody transcribes a notice published
late in the year before. Ninety days is roughly when that notice exists.

`not declared` does not by itself fail the command. No claim was ever made about
that venue, which is a different state from a claim that expired — the same
distinction the capability record draws between an unstated coverage floor and
an unbounded one. The closures may well be transcribed; what is missing is
anybody saying how far.

**Coverage is declared, never inferred.** It used to be read off the furthest
recorded closure, and that was wrong in both directions at once. A calendar
transcribed through 2026 reported its reach as 2 September — the year's last
public holiday — so the final quarter read as uncovered while its transcription
sat in the table. And every date *before* that closure read as covered,
including years holding no rows at all: a 2016 series was checked against a
calendar with no 2016 closures in it, and three real Vietnamese public holidays
were raised as missing sessions. The claim now lives on the venue and comes from
`MarketData__TradingCalendarCoveragePath`.

### `dataset`

```bash
pqt dataset export --universe DEMO_INDEX --from 2026-01-02 --to 2026-09-05 --as-of 2026-01-02
pqt dataset verify 6a233e778e940860 --version 1
```

Builds the canonical research dataset, and checks one still is what it claims to
be. An operator command rather than an endpoint: an export walks every
constituent of a universe across a date range and writes files to the
deployment's own disk, which is a scheduled job's shape and not a web request's.

A build lands at `{Datasets:Directory}/{dataset_id}/v{version}/` as
`bars.parquet` beside `manifest.json`. The identifier is **derived from the
parameters**, not issued, so the same request always names the same dataset and
a rebuild is a new version of it rather than a new dataset.

| Option | Default | What it decides |
| --- | --- | --- |
| `--universe` | required | Where the instrument set comes from |
| `--from` | required | First session, inclusive |
| `--to` | today | Last session, inclusive |
| `--as-of` | the window's end | When the constituent set is read |
| `--interval` | `1d` | Bar resolution |
| `--raw` | off | Export what printed rather than the adjusted series |
| `--known-as-of` | current series | Read the bars as this system believed them then |
| `--policy` | `strict` | What to do with an unknown announcement date |

`--as-of` defaults to the **end of the window**, not to today. A universe read
today against a window that ended in 2018 exports the index as it now stands,
which is survivorship bias assembled by hand — everything removed since is
silently absent.

`--policy` defaults to `strict`, the opposite of the chart's default. A dataset
is run against repeatedly by things nobody has written yet, so look-ahead baked
into one propagates into every experiment that reads it and shows up as a better
result, which nobody investigates. See
[ADR-022](../architecture/decisions/ADR-022-announcement-aware-adjustment.md).

**An unknown universe membership refuses the export.** Not an empty dataset — an
empty one produces a backtest that reports no positions and no error:

```
Membership of VN30 on 2016-12-31 is not known (NoSuchUniverse). Declare the
universe's coverage and import its history before exporting a dataset over it.
```

`verify` recomputes every file digest **and** the manifest's own content hash.
Checking only the files would let somebody change the as-of a dataset claims, or
the policy it was built under, and leave every hash matching. A recorded digest
that is never checked is a comment.

Each export prints the licence note recorded against every contributing source.
The default says the true position: no licence was granted, the rows are
retained under the absence of a prohibition rather than a permission, and they
are not for redistribution. Configure a real one per source under
`Datasets:LicenceNotes` if a deployment ever obtains one.

The store is a **named volume**, never a bind mount into the repository, for the
same reason: an export holds real market prices. Copy one out deliberately.

```bash
docker compose cp backend:/app/datasets/6a233e778e940860/v1 ./exported
```

---

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | The command ran and the answer was affirmative |
| `1` | The command ran and the system refused, failed, or found nothing |
| `2` | The command line itself was wrong, and nothing was attempted |

The distinction between 1 and 2 is what a script needs. A malformed command is
the operator's mistake and re-running it will fail identically; a refused run is
the system's answer about the data and may well succeed tomorrow. A retry loop
that could not tell them apart would retry the typo.

**A command line the operator got wrong never reaches the deployment.** Nothing
is resolved from the container until a command has accepted its own arguments,
so a mistyped option is answered with the option rather than with a database
error. An option no command recognises is refused rather than ignored: a
silently dropped `--from` is a backfill over the wrong range that reports
success, and the audit trail it leaves is indistinguishable from a correct run.

Every log line goes to standard error whatever its level, so a pipe reads the
command's answer alone.

A command that fails answers with the failure and not with its stack. An
unreachable database produces thirty frames through the connection pool, EF Core
and the query pipeline, none of which say anything the first line does not, and
a surface that prints them by default is one an operator stops reading. The
trace is logged at debug and stays one variable away:

```bash
Logging__LogLevel__Default=Debug pqt schema status
```

---

## Worked example — a first real backfill

```bash
# Is this deployment the one you think it is?
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll schema status
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll calendar status

# What can this deployment read, and does it adjust prices itself?
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll provider list

# Would this source serve this instrument at this resolution?
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll \
    provider check VCI --instrument FPT --interval 1d --from 2021-01-01

# Fetch it, one instruction, with an audit trail naming the range asked for.
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll \
    ingest backfill --instrument FPT --interval 1d --source VCI --from 2021-01-01

# What did the quality rules find?
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll \
    quality list --instrument FPT

# Close one, once you know what accounts for it.
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll \
    quality resolve <ID> --explained \
    --reason "2 Jan 2026 was swapped to Sat 10 Jan by decree; the calendar was wrong."
```

Back up the database before the first real ingest against a source that has
never been read here. See [`database-backup.md`](database-backup.md), which
includes the restore step — a backup nobody has restored is a hypothesis.

---

## What the CLI is not

It is not an alternative core. A command that computed something no other caller
can reach would be the layering having been breached, and the same
one-core-many-interfaces rule governs the REST API, the Python facade and the
eventual MCP server.

It is not authenticated. There is no user model until Phase 19, so anyone who
can reach the container can run these commands — including `quality resolve`,
which closes findings. That is acceptable for a single-operator deployment and
is not acceptable for a shared one.
