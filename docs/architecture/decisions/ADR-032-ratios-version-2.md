# ADR-032: Ratios version 2 — capital steps without an action, and dividend coverage that is recorded

**Status:** Accepted (Dev agreed with four edits, applied) · **Date:** 2026-10-21 · **Phase:** Sprint 8, items 8.1 and 8.4 (Phase 6)

Supersedes the dividend-yield coverage rule and the "step with no action is not
checked" sentence of [ADR-030](ADR-030-point-in-time-ratios.md). The rest of
ADR-030 stands.

## Context

Sprint 7 closed with two things ADR-030 left open and the evidence made plain.

**A capital step nobody explains.** ACB's charter capital rose 15.0% between the
2023Q1 and 2023Q2 balance sheets (3,377,435,100 → 3,884,050,400 shares) with no
corporate action recorded. ADR-030 checks a step only when an action sits near
it, so no ACB row carries a finding (0 of 378 rows). The number is probably
right, because capital comes from the balance sheet, but the reader of a
dataset cannot tell that a 15% step happened at all except by comparing rows.

**A dividend history that was never complete.** `data/fixtures/corporate-actions.csv`
holds 75 rows, sourced in sprint 5 to explain price-band breaches. A cash
dividend of 1–5% never breaches ±7%, so most were not recorded: FPT has 3 cash
dividends since 2016, GAS 1, SAB none, VCB none. ADR-030 tests coverage by "the
issuer's earliest recorded action is at least 365 days before `t`". That
treats the first recorded action as proof that everything after it is recorded,
which the sprint 5 sourcing method contradicts. The dividend yields exported
today are mostly understated (FPT on 2024-12-02: 0.70%).

Share-count actions are far better covered: a bonus or stock dividend of 5% or
more breaches the band, and an ESOP issue of about 1% is absorbed by the 2%
tolerance.

## Decision

**Ratios move to `ratio_version` 2.** Two changes, shipped together so the
version is bumped once.

### 1. A capital step with no action is a finding

For a row at session `t`, take the latest known balance sheet and the one
before it (same scope). If capital moved by more than 2% (`|step − 1| > 0.02`)
and **no share action announced by `t` has an ex-date between the two
quarter ends**, the row carries the finding. Where such an action exists,
ADR-030's three outcomes speak and this one does not, so a step is never
reported twice. "The one before it" means the previous quarter with stated
capital, and capital is read by the same rule as ADR-030 (411a, or 411):

`capital step +15.0% between 2023Q1 and 2023Q2 with no corporate action that explains it`

with both capital figures. Rules:

- It is a finding only. Shares, and every ratio, are unchanged. Nothing is
  estimated and no action is invented.
- It appears on every row whose latest balance sheet is the later of the two,
  so it lasts as long as that balance sheet is the latest, and goes when the
  next one arrives.
- It covers the case ADR-030 left out: no action with an ex-date in the
  interval. It does not replace ADR-030's three outcomes.
- If the scope differs between the two balance sheets, the step is not
  compared (a separate and a consolidated capital are not comparable).
- MBB's two capital values for 2022Q4 and 2023Q4 come from a repeated prior
  period form (ADR-029). The as-of read already picks one report. Whether the
  step check flags the quarter is a test case, not a rule.

### 2. Dividend coverage is a recorded fact, not an inference

The 365-day proxy is removed. Dividend yield is present only for a name with a
**coverage record** and only when the trailing year lies inside it.

`data/fixtures/corporate-action-coverage.csv`, one row per name:

| column | meaning |
| --- | --- |
| `ticker` | the name |
| `complete_from` | the date from which every corporate action of the name (share-count and cash) is recorded |
| `swept_on` | the date the sweep was done |
| `source` | where the notices came from (VSD, issuer disclosures) |

A sweep proves completeness only up to the day it was done, and `complete_from`
is about **ex-dates**: the sweep covers every action with `ex_date ≥
complete_from`, including one announced before that date, so the notice list
is read back far enough (about two months) to catch them.

Rule: dividend yield at `t` is present when the name has a record,
`t − 365 days ≥ complete_from` **and `t ≤ swept_on`**. Otherwise it is absent with the new code
`dividend_history_incomplete`. A name with no record is absent. A present yield
may be zero, because inside the coverage zero is known.

Sprint 8 sweeps **eight names**: BCM, MSN, MWG, PLX, POW, VNM, VPL and VRE, which
had no action at all. Every action type of those names is transcribed from
2023-07-01 (bonus, stock dividend, cash dividend, rights, split). Each name's
`complete_from` is the date the sweep can vouch for, written down with the
earliest notice read; it is 2023-07-01 at the earliest, and VPL starts at its
listing on 2025-05-13. Notice references go in
`data/fixtures/sources/entitlements-eight-names.md`, one line per row, as
sprint 5 did, so the corporate-actions CSV keeps its schema. The other 18 names of the export are **not** swept: their yield is
absent until a later sprint sweeps them. That is the Moderator's choice of
2026-10-21; the cost is that the dividend yield is a thin column for now.

`NoDividendCoverage` (`no_dividend_coverage`) is kept for version 1 data and for
the reader. Version 2 writes `dividend_history_incomplete` instead.

### 3. What changes in the dataset and the service

- `ratio_version` is 2 for exports written after this ADR. A version 1 dataset
  on disk still verifies and says which version it carries.
- The Python reader accepts `ratio_version` 1 and 2 and still refuses any other.
  The `_absent` code set grows by `dividend_history_incomplete`; the parquet
  columns are unchanged.
- The coverage record is **imported and read like corporate actions**, not read
  from the file: a table `quant.corporate_action_coverage` (instrument_id,
  complete_from, swept_on, source) with a migration, an import beside the
  corporate actions (ticker resolved through the file alias), and a repository
  read. The export and the API stay off the file system.
- The ratio service carries the capital-step finding in `findings` and the
  coverage rule above in place of the proxy. The console, CLI and API show the same code.
- Share counts change only where new actions are recorded for the eight names.
  The "no corporate action recorded; share count not adjusted" finding goes
  away for a name that now has actions and stays for a name that has none.
- The manifest schema is unchanged (v4). Only the values it accepts for
  `ratio_version` and the content hash input change, as a number.

## Alternatives

- **Keep the proxy and add a warning.** The yield would still be wrong in the
  direction the evidence shows (understated), and a warning does not stop a
  factor from using it. Rejected.
- **Sweep all 26 names now.** About 150–250 notices, a sprint-sized data task.
  The Moderator chose the eight names; a full sweep can follow when yield is to
  be a factor input.
- **Correct ACB's share count by inferring a missing action.** Estimating an
  action from a step makes a guess look like a record. Rejected, as in ADR-030.
- **Absent yield for every name until a full sweep.** Safer, and wastes the
  eight names that are swept. Rejected in favour of the per-name record.

## Trade-offs

- **Yield is mostly absent for now.** Only eight names, from mid-2024 (a year
  after `complete_from`). Value factors lean on E/P and B/P in Phase 7, not on
  yield, which is why this cost is accepted.
- **Hand transcription.** The eight names' notices are read by a person, as in
  sprint 5. Each row names its source so a later reader can audit it.
- **A sweep ages.** Yield is absent after `swept_on` until the name is swept
  again, so a dataset read later does not count a dividend paid after the sweep
  as "zero known".
- **A step finding can fire for a legitimate reason.** A private placement or an
  ESOP issue moves capital with no bonus action. The finding says what it saw
  and nothing more.

## Consequences

- ADR-030's status becomes "Accepted · partly superseded by ADR-032".
- Dev implements 8.1 and 8.4 under one `ratio_version` 2 bump. Tests:
  an ACB-style step yields the finding on the right sessions and no others;
  a matched action yields none; a name with a coverage record and `t − 365`
  after `complete_from` has a yield, a name inside the first year or without a
  record has `dividend_history_incomplete`; a version 1 dataset still verifies
  in both languages and a version 2 one round-trips.
- The feature catalogue (Phase 7) is written after this: value features read
  P/E and P/B at `ratio_version` 2.
- Open: a sweep of the other 18 names, and whether yield becomes a factor input.
