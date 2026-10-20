# ADR-031: Fundamentals in the canonical dataset

**Status:** Proposed · **Date:** 2026-10-20 · **Phase:** Sprint 7, item 7.3 (Phase 6)

## Context

ADR-023 made the dataset the one path research reads: Parquet plus a manifest,
hashed, with no second read path beside it. ADR-024 and ADR-025 made it
point-in-time for membership and history. Fundamentals are the next thing a
factor will want (value, quality), and they exist today only through the
console, the CLI and the API.

If a factor reads fundamentals through the API, it has a second read path with
its own cut, its own scope choice and its own share count, and two experiments
can disagree about the same session. ADR-029 and ADR-030 made the reads honest;
this ADR decides how they travel in the dataset so that nothing else is needed.

Two facts shape the design:

- Ratios depend on the raw close of the session, the statements known when the
  session opened, and the corporate actions announced by then (ADR-030). Each is
  a function of the session, not of the window, so the value must be computed
  per session.
- The names that have no fundamentals (STB, GVR, and by scope the securities
  firms and the insurer) are a different thing from a name whose ratios are
  all absent for a quarter. A reader must be able to tell them apart.

## Decision

**An export may carry fundamentals as a second file, computed per session by
the same service the surfaces use.**

`pqt dataset export ... --fundamentals` (default off). When on, the export also
writes `fundamentals.parquet` next to `bars.parquet`: one row per
`(instrument_id, session)` for every **membership** session of every exported
instrument. History rows (ADR-025) get no fundamentals row, because they are
prices a lookback may read and not sessions anything is scored on.

Seven things are decided:

**1. One computation.** Each row is `IFundamentalRatioService.ReadAsync` for
that instrument and session (ADR-030). There is no second implementation in the
export or in Python. A change to how a ratio is computed is a change to
`ratio_version`, below.

**2. The cut is 00:00 UTC of the session date, bounded by the dataset's
observation instant.** That is 07:00 in Hanoi, before the session opens, and
it is the cut the surfaces already use (`session_date_00utc`; the manifest says
so). When the export has a `known_as_of` (ADR-023), the row is read at the
earlier of the two instants, so a dataset reproduced for a past date cannot see
a report filed after it. The ratio service takes no external instant today; it
gains an optional `knownAsOf` parameter (`min(00:00 UTC, knownAsOf)`) as part of
this item.

**3. Columns.** `instrument_id`, `session`, `known_as_of_utc`, `scope`,
`balance_sheet_quarter`, `report_ids` (the reports behind the balance sheet),
`ttm_first_quarter`, `ttm_last_quarter`, `actions_applied`, `actions_withheld`,
`close_raw`, `shares`, `net_profit_ttm`, `eps_ttm`, `book_value_per_share`,
`pe`, `pb`, `roe`, `dividend_yield`, and `findings` (a list of strings: the
results of the charter-capital and par checks of ADR-030). These carry what
ADR-030 promises, that each ratio names the report, the quarters and the
actions it used. Every number is written as a string, as ADR-023 decided for
prices, and a value that is absent is null. For each ratio a companion
`<name>_absent` column carries a code from the `RatioAbsence` enum, and is null
when the value is present. `RatioAbsence` (`no_balance_sheet`,
`no_charter_capital`, `missing_quarter`, `trailing_loss`, `no_close`,
`equity_not_positive`, `no_dividend_coverage` and the like) is introduced in
ADR-030's item B, returned by the service in place of a free-text reason, and
is the set this column holds. A reader never sees a zero standing in for
"unknown".

**Scope.** IDS shows one form for a separate and a consolidated filing of the
same period (ADR-029, correction of 2026-10-20), and the importer keeps values
for the consolidated one only. So the export reads **consolidated** statements
and falls back to **separate** only for a name with no consolidated values
(LPB, and TPB for some periods). `scope` is `consolidated` or `separate` and is
never mixed inside one ratio's inputs; null when no statement is known. A
separate report whose form was identical to the consolidated one carries no
values and is never a source.

**4. The manifest is schema version 4.** It adds a top-level
`fundamentals` object, or null when the export carried none:
`ratio_version` (an integer; ADR-030 is 1), `source` (the provider code, with the
licence note ADR-029 records), `cut` (`session_date_00utc`), `rows`, and
`not_collected`, the instrument ids with no report held at all, each with a
reason: `ids_capture_gap` (STB, GVR) or `out_of_scope` (securities firms and
the insurer, such as SSI, TCX and BVH). A reader can then tell a gap that may be
fixed from a name outside the first import's scope. `fundamentals.parquet` is listed in `files` with its hash, like
`bars.parquet`. The contract is
`docs/schemas/dataset-manifest-v4.schema.json`.

**5. The content hash covers the new fields from version 4 only.**
`fundamentals|` renders the object in a fixed order after the history fields
(or `none`), as ADR-025 did for history. A version 1, 2 or 3 manifest renders
none of it and hashes exactly as it was written, so every dataset on disk keeps
verifying. The two languages are held to one committed fixture,
`quant/tests/fixtures/manifests/v4-fundamentals.json`.

**6. Existing requests keep their identifiers.** `|fundamentals` is appended to
the canonical request string only when the flag is on. A dataset with
fundamentals is a different dataset from the same window without them.

**7. The reader enforces the contract.** A reader refuses: a fundamentals row
on a session the instrument was not a member; a row for an instrument in
`not_collected`; a `ratio_version` it does not know; a value present together
with its `_absent` reason; a v4 manifest listing `fundamentals.parquet` that is
missing from disk. `members_on` and the `membership` view are unchanged:
fundamentals never make an instrument eligible.

## Alternatives

- **Export the statement lines and let research compute the ratios.** Moves
  ADR-030 into Python, so there are two implementations that must agree. It also
  makes every consumer re-solve the share count, the scope fallback and the
  trailing-twelve-month rule. Rejected for the reason ADR-023 gives for a
  second read path.
- **One row per change (an event table) with forward-fill.** Smaller, but a
  ratio that depends on the close changes every session, so the forward-fill
  would be wrong for exactly the ratios people use. Rejected.
- **Put the columns in `bars.parquet`.** Changes the schema every reader
  already holds, and adds rows to the file that means "membership". ADR-024 and
  ADR-025 rejected the same shape.
- **Always on.** Costs one service read per instrument and session on every
  export, including the ones that never touch fundamentals. Opt-in keeps
  existing exports and their identifiers as they are.
- **A separate dataset id for fundamentals.** Splits one experiment's inputs
  across two manifests that must be kept in step, which is the situation the
  manifest exists to prevent.

## Trade-offs

- **Size.** 30 names over several years is tens of thousands of rows of
  strings. That is small beside the bars, and a string column compresses well.
  If a universe grew to the point it mattered, a column encoding is a later
  schema, not a reason to complicate this one.
- **Export time.** One service read per row would repeat the same loading. The
  service gains `ReadSeriesAsync(instrument, sessions, knownAsOf)`: it loads an
  instrument's report rows, adjustments, actions and bars once and cuts them in
  memory per session. The result must equal `ReadAsync` called per session, and
  a test holds it to that on real data for three names. It is an optimisation
  inside the service and does not change this contract.
- **Ratios depend on the close.** A dataset exported with `--no-adjust` or with
  a different adjustment policy still carries the raw close. The ratio uses the
  raw close by ADR-030, so `close_raw` is not the price column of the bars when
  those are adjusted. The name says so.
- **Known gaps travel.** STB, GVR and the two HPG filings are not collected.
  The export marks STB and GVR in `not_collected`. HPG has reports, so it has
  rows, and the quarters it lacks make some of its TTM values absent with
  `missing_quarter`. That is honest, and it is visible.

## Consequences

- Research reads fundamentals through `members_on` and the dataset, not the
  API. The API and CLI stay the operator's surfaces.
- The Python reader gains a `fundamentals` view and the integrity checks in
  decision 7. It does not compute any ratio.
- ADR-030's two promised checks (charter capital across quarters, par against
  reported EPS) must land before the first v4 export is used for research. If
  they land after, `ratio_version` becomes 2 and old v4 datasets still verify
  and still say which version they carry.
- Tests: a v4 manifest round-trips in .NET and in Python against the same
  fixture; a v3 manifest still verifies; an export of three names shows the
  as-of property by a synthetic restatement (a row after `reported_at` differs,
  a row before does not); a session before an instrument's first report has all
  ratios absent with a reason; an instrument in `not_collected` has no rows.
- Open for the Moderator: whether `--fundamentals` should become the default
  once the checks above have landed.
