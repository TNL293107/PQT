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

**`ingest` needs Redis as well as PostgreSQL.** Every pass holds its series in
Redis so it cannot race the scheduled host — see [`ingest`](#ingest). Inside
the deployment that is already arranged. On the host, point the CLI at the
ports Compose publishes, which are the `*_HOST_PORT` values in `.env`:

```bash
export Postgres__Host=localhost Postgres__Port=5432        # POSTGRES_HOST_PORT
export Postgres__Database=personal_quant Postgres__Username=quant_user
export Postgres__Password=...                              # POSTGRES_PASSWORD
export Redis__Host=localhost Redis__Port=6379              # REDIS_HOST_PORT
export Redis__Password=                                    # REDIS_PASSWORD, if set
```

PowerShell: `$env:Redis__Host = 'localhost'`, and so on. A Redis the CLI cannot
reach does not stop the command from starting, and does not let it ingest
unlocked either: each pass is recorded as failed, naming the lock store, and the
command exits 1. The commands that never ingest — `provider`, `quality`,
`coverage`, `schema`, `calendar`, `dataset`, `universe` — do not touch Redis.

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

`--to` names the **last session included**, on both verbs, as it does on
`dataset export`. (It used to be the exclusive end, so the named day was never
fetched; see below.)

`backfill` is a loop over `run`, not a second pipeline. The service truncates a
range longer than one call may carry (65 days from CafeF), so a backfill walks
the range one window at a time: **each pass starts where the one before it
stopped**, never at the checkpoint. The walk ends at `--to`, or, with none, when
the pipeline skips a pass because no finished period is left. A window the
source returned nothing for is walked past rather than ending the run, and
`--max-passes` is a second stop for the case nobody predicted.

Both rules were learned the hard way on the two-year VN30 backfill. Passes
used to resume from the checkpoint. On a series that already held later bars,
the second pass jumped past the gap it was asked to fill, and ACB stored 45 bars
of ~480 while reporting success. And `--to` as an exclusive end silently dropped
the last session of every spell it was used to close. The checkpoint itself
still never moves backwards, so filling an old gap does not make the scheduled
run re-ingest history.

**Name `--source` whenever two registered sources could serve the request.**
There is no fallback and no priority order: ambiguity is refused, and the run
records both candidates rather than picking by registration order.

**One pass per series at a time, across processes.** Each pass holds its
instrument and resolution under a lock in Redis until it ends, so an `ingest`
started while the scheduled host is working the same series does nothing and
says so:

```
FPT 1d CAFEF ... skipped fetched 0 accepted 0 rejected 0 stored 0 revised 0
FPT: Another ingestion of this series is in progress, so this pass did not run. ...
```

That is a refusal, exit 1, and on `backfill` it stops the instrument rather than
being read as the end of the range. Re-run once the other pass has finished; it
resumes from the checkpoint that pass leaves. A different resolution of the same
instrument is a different series and is not excluded.

If Redis cannot be reached the pass does not ingest at all — it is recorded as
**failed**, with a reason naming the lock store, rather than proceeding unlocked.
A lock whose holder died is released by its expiry,
`MarketData__IngestionLockTtlSeconds` (ten minutes by default); the setting is
validated to outlast the longest pass the retry policy permits, so raise it if
you raise `MaxAttempts` or `ProviderTimeoutSeconds` far enough to need it.

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

### `coverage`

```bash
pqt coverage <TICKER|INSTRUMENT-ID> [--exchange <VENUE>]
```

What PQT holds for one instrument, dataset by dataset — the Phase 5 coverage
registry ([ADR-026](../architecture/decisions/ADR-026-market-intelligence-terminal.md)).
The word after `coverage` is the security: a ticker resolved through the
instrument master, or an instrument identifier read as is. A ticker live on two
venues is refused with both named; pass `--exchange` or the identifier.

```text
FPT  FPT Corporation  HOSE  01a129bf-1d0c-7473-a773-85b7ccbd4d64

DATASET            STATE            EXTENT                   DETAIL
Price 1d           ✓ held           2015-01-02 → 2026-08-28  2,900 bars · VNX · 1 of 2,901 sessions missing · 1 open finding · last attempt 2026-08-29 VNX Succeeded
Price 1h           ✓ held           2026-08-03 → 2026-08-07  30 bars · VNX · missing sessions not measured
                                                             Missing sessions are measured for the daily series only.
Price 5m           — not collected                           No registered source serves 5m bars for HOSE Equity.
Corporate actions  ✗ not held                                No corporate action is recorded for this instrument.
Universe VN100     ✗ not held                                Not a member at any time from 2024-01-01 onward, the window VN100 declares; outside it, unknown.
Universe VN30      ✓ held           2024-01-02 → now         1 spell
                                                             VN30 claims to know its membership from 2024-01-01 onward.
Universe VNMID     ? not measured                            VNMID declares no coverage, so the absence of a spell says nothing about membership.
Fundamentals       — not collected                           Phase 6

✓ held · ✗ not held (known absent) · ? not measured (cannot say) · — not collected yet
```

Every dataset is in exactly one of four states, printed as a mark **and** a word
so a terminal that drops the mark still keeps them apart:

| Printed | State | Means |
| --- | --- | --- |
| `✓ held` | `Held` | PQT holds it; the extent is on the line |
| `✗ not held` | `NotHeld` | PQT collects this kind of data and holds none for this instrument |
| `? not measured` | `NotMeasured` | Whether PQT should hold it cannot be said |
| `— not collected` | `NotCollected` | PQT does not collect this kind of data yet |

**`? not measured` is never `✗ not held`.** A universe that never declared its
coverage window cannot say an instrument was not a member, so a spell-less
instrument is *not measured* there; it is *not held* only in a universe that
declared a window, and only for that window — the line says which.

**Missing sessions are counted only where they can be.** For the daily series,
against the venue calendar's trading days between the first and last session
held, and only when the calendar was transcribed for that whole span. Otherwise
the line says *missing sessions not measured* rather than printing zero. A bar
stored on a closed day is not counted, so it cannot hide a genuinely missing
session. Intraday series are not counted in sessions at all.

A price resolution is *not collected* when no registered source serves it for
the instrument's venue and asset type; `pqt provider list` shows what is
registered. Bars already stored are *held* whatever is registered now.

The same answer is served at `GET /instruments/{id}/coverage` and by the
console's `COV` function. All three call one application query, so they cannot
disagree about what a state means.

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
pqt dataset export --universe VN30 --from 2026-07-01 --to 2026-09-17
pqt dataset export --universe VN30 --from 2026-08-03 --to 2026-09-17 --as-of 2026-08-03
pqt dataset export --universe VN30 --from 2026-07-01 --to 2026-09-17 --history 253
pqt dataset verify 9d1609783db37729 --version 1
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
| `--as-of` | none: point-in-time | Read one constituent set on this date instead |
| `--interval` | `1d` | Bar resolution |
| `--raw` | off | Export what printed rather than the adjusted series |
| `--known-as-of` | current series | Read the bars as this system believed them then |
| `--policy` | `strict` | What to do with an unknown announcement date |
| `--history` | `0` | Bars to write before each name's first membership session (at most 1000) |

**Without `--as-of` the export is point-in-time**: a bar is written only where
its instrument was a member of the universe that session, and the manifest lists
each instrument's spells. Over VN30 from July to September 2026 that is exactly
30 members on each of 54 sessions, with PLX and TPB until 3 August and MCH and
TCX from it. The whole window must lie inside the universe's declared coverage,
or the export is refused rather than exporting only the names that happened to be
sourced. See
[ADR-024](../architecture/decisions/ADR-024-point-in-time-dataset-membership.md).

**`--history N` gives each series the past a lookback needs.** Without it a name
admitted mid-window has no bar before its admission, so a factor reading a year
of prices cannot score it for a year. With it the export also writes the N bars
before each instrument's first membership session (before the window, for
`--as-of`) and every bar between two of its spells, so the series is continuous
up to its last membership session. Those rows are **data, never membership**:
the spells still say who belonged when, and a reader refuses any row outside them
that is not history. The manifest records `history_sessions` and each
instrument's `history_from`; a request with history names a different dataset
from the same window without it. See
[ADR-025](../architecture/decisions/ADR-025-dataset-history-rows.md).

`--as-of <date>` asks for the older reading instead: one constituent set, read on
that date, with every bar its members have anywhere in the window. It is right
for a window inside one basket and wrong across a review, which is why it has to
be named. The date may not fall after the window's end, which would apply a
later index to earlier prices.

`--policy` defaults to `strict`, the opposite of the chart's default. A dataset
is run against repeatedly by things nobody has written yet, so look-ahead baked
into one propagates into every experiment that reads it and shows up as a better
result, which nobody investigates. See
[ADR-022](../architecture/decisions/ADR-022-announcement-aware-adjustment.md).

**An unknown universe membership refuses the export.** Not an empty dataset — an
empty one produces a backtest that reports no positions and no error:

```
Membership of VN30 is not known on every session from 2025-07-01 to 2025-09-30
(OutsideCoverage). Narrow the window to the universe's declared coverage, or
source the rest of its history first.
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

### `research`

Queues research work for the quant worker (ADR-016) and reports on it. The CLI
never runs research itself: the worker claims the job in its own container,
connected as `quant_research`, and records the outcome.

```bash
pqt research submit momentum-slice --dataset 3856e4ce77841c1a [--version 3] [--quantiles 10] [--rebalance-every 21]
pqt research list [--limit 20]
pqt research show <JOB-ID>
```

`submit` refuses a dataset that has no exported build, or a named build that
does not exist, rather than queuing a job to fail later. Without `--version`
the worker runs the latest build at the moment it claims the job. `show` exits
1 for a failed job, so a script can wait on one and tell the outcomes apart.
Start the worker with `docker compose --profile research up -d quant-worker`;
it needs `RESEARCH_DB_PASSWORD` in `.env`, the same value the backend uses to
give the role its login.

### `universe`

Lists the universes a deployment defines, and counts how one moved on a session
(ADR-026). The count is the application layer's — the same one
`GET /universes/{code}/breadth` and the console's `BREADTH` render.

```bash
pqt universe list
pqt universe breadth <CODE> [--on yyyy-MM-dd]
pqt universe breadth --venue <VENUE> [--on yyyy-MM-dd]
```

`list` shows each universe's kind, the coverage it declares, and how many
members it has today. A universe whose coverage does not reach today shows
`unknown`, never `0`: its membership today is not known, which is not the same as
empty.

```text
CODE  KIND   COVERAGE      MEMBERS 2026-10-11  NAME
VN30  Index  2026-07-01 →  30                  VN30 Index
```

`breadth` prints a summary line, the volume behind each side, the session each
member was measured against, and then every member sorted by change, members
without data last (the figures below are illustrative, not market data):

```text
VN30 2026-09-30  ▲ 18  ▼ 9  = 2  no data 1
volume  ▲ 41,220,300  ▼ 18,904,100  = 1,250,000
against HOSE 2026-09-29

   TICKER  CHANGE   CLOSE   PREV    VOLUME     NOTE
▲  MWG     +3.85%   67,500  65,000  6,120,400
…
=  REE     0.00%    68,000  68,000  310,200    ex-date: previous close 70,000 restated
▼  VJC     -2.10%   93,300  95,300  1,402,000
·  BSR     no data  unknown 21,450  unknown    no bar on the session
```

What it counts, and what it refuses:

- **Members** are those whose spell contains the session, read through the
  universe's coverage claim. A session the universe does not claim to know is
  refused rather than counted over an empty set.
- **The previous session** is the previous trading day on each member's own
  venue calendar. The session must be a trading day on every venue a member
  trades on; a weekend or recorded closure is refused, and so is a session — or
  a previous session — the calendar was not transcribed for.
- **The change** is measured on prices adjusted for actions going ex on the
  session: the previous close is multiplied by their factor and, for an equity,
  rounded to the venue's tick, which is the exchange's own reference-price rule.
  An ex-dividend gap is therefore not a decline, and the row says the previous
  close was restated. This differs from a broker's board on an ex-date,
  deliberately.
- **No data** is a member without a bar on the session or on the previous one.
  It is never counted as unchanged.
- Without `--on`, the session is the latest one up to today on which some
  member — a member that day — has a daily bar, looking back at most 31 days.
  The output says it chose. If no member has a bar in that window, the command
  refuses rather than reaching further back.

A refusal exits 1 with its reason on standard error; an unknown universe code is
a refusal too, since whether one exists is the deployment's answer.

`--venue HOSE` counts the same quotes over every equity listed on the venue that
session instead of a universe's members (also `GET /venues/{code}/breadth` and
the console's `breadth --venue HOSE`). The members are the equities **this
deployment's instrument master** holds on the venue, which is not every name the
exchange lists, and whose record says they were listed that day: a pending
record is out, a delisted one is out after its last trading day, and one with no
recorded listing date is counted. A line under the totals says how many were
counted without a listing date and names those with no daily bar ever ingested
— they can only be no data. The venue is the one each instrument is on now; a
name that moved here is read as having always been here, until venue history is
recorded.

### `watchlist`

The operator's watchlists (ADR-026), the same ones the console shows. Stored by
canonical instrument, so a ticker change leaves a list pointing at the same
security.

```bash
pqt watchlist list
pqt watchlist create core
pqt watchlist add core FPT VCB HPG
pqt watchlist remove core HPG
pqt watchlist show core
pqt watchlist quotes core --on 2026-09-30
pqt watchlist rename core vn30-core
pqt watchlist delete vn30-core
```

Names are lowercase letters, digits, `_` and `-`. `add` and `remove` resolve
every ticker before writing anything, so a typo leaves the list untouched;
adding what is already there and removing what is not are reported, not
refused. The CLI needs no operator key — it writes with the deployment's
database credentials — but the browser does: set `PQT_OPERATOR_KEY` in `.env`
for the API to accept writes at all (ADR-027).

`quotes` prints each instrument's close, change and reference on one session, in
list order — the quotes `universe breadth` counts, so a name reads the same in
both. The reference is the previous close restated for any action going ex that
day and rounded to the venue's tick, as the exchange publishes it; a row
restated that way says so. Without `--on`, the session is the latest any
instrument on the list has a bar for, and a name without one that day is
`no data`, not its older close. A holiday, a session the calendar does not
reach, or no recent bars at all is a refusal and exits 1.

### `workspace`

A workspace is a named list of console lines the console replays with
`ws load <NAME>` — what was asked, never what was answered, so replaying it
tomorrow shows tomorrow's data.

```bash
pqt workspace save morning "breadth VN30" "FPT GP" "wl show core"
pqt workspace list
pqt workspace show morning
pqt workspace delete morning
```

Saving under an existing name replaces its lines. At most 50 lines of 256
characters each, without control characters.

### `fundamentals`

Records disclosed financial reports from one named provider
([ADR-029](../architecture/decisions/ADR-029-fundamentals-source.md)). `IDS`
reads the documents [tools/ids-collector](../../tools/ids-collector/README.md)
wrote to `MARKET_DATA_IDS_FILINGS`. Nothing is read without `--provider`.

```bash
pqt fundamentals import --provider IDS
pqt fundamentals import --provider IDS --ticker FPT,VCB
```

Each filing is classified, normalised and dated, then saved on its own:

- **Classified.** Its kind and scope come from its name, checked against the
  form's period.
- **Normalised.** Each column is placed in its period and span. A blank cell
  is no value, and an all-zero statement was not filed.
- **Dated.** `known_from_utc` is the next local midnight after the IDS date.

A filing already held is read again. A changed value supersedes its reading;
nothing is overwritten or removed. The raw document is retained whenever its
checksum is new.

The run prints two tables:

- **late** lists filings dated past their Circular 96 deadline. IDS dates can
  trail the exchange's, so this measures the record, not the issuer.
- **refused** lists filings that were not recorded:

| Reason | Means |
| --- | --- |
| `UnknownInstrument` | The ticker is not an active instrument |
| `Unclassified` | The name does not say its kind, or says both scopes |
| `PeriodConflict` | The form's period contradicts the name |
| `ImplausibleDate` | Filed before its period ended, or an "audited year" filed within 45 days of year end |
| `Unreadable` | A tab, heading, form or cell the importer does not read |
| `ConflictingValues` | One line stated twice with different values |
| `ValuesAppeared` | A report recorded without values now has some |

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

# What does PQT now hold for it, and how much of the span is missing?
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll coverage FPT

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
