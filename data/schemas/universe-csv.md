# Universe CSV

The interchange format the file-backed universe source reads: which named sets
of securities exist, and which securities belonged to them over which dates.

It exists as a file source for the same reason the corporate action one does.
Vietnamese index membership is published as review notices — one announcement
per review, in a PDF — rather than served from an API, and any system that
wants the history ends up transcribing it.

Configure it by pointing `MarketData__UniverseDirectory` at a **directory**
holding both files below. With that setting blank, no source is registered, no
universe is recorded, and every constituent read is unknown — see
`.env.example`.

## Two files, because they carry two different claims

| File | What it states |
| ---- | -------------- |
| `universes.csv` | What each set is, and **which span of its history this directory is supposed to contain** |
| `universe-memberships.csv` | The history itself |

The split is the point. Membership rows cannot say whether an index had no
constituents in 2018 or whether nobody sourced 2018, and those are opposite
answers that a list of rows expresses identically. The coverage claim is the
operator asserting what the file is meant to hold, so a read outside it can
answer *unknown* instead of returning an empty set.

Deriving the claim from the rows — `MIN(effective_from)` — was rejected: a
history sourced with a hole in the middle would look continuously known.

## `universes.csv`

A header row is required. Columns are matched **by name**, not by position.

| Column           | Required | Meaning                                                     |
| ---------------- | -------- | ----------------------------------------------------------- |
| `code`           | yes      | Short code — letters, digits and underscores, e.g. `VN30`    |
| `name`           | yes      | Full name                                                    |
| `kind`           | yes      | `Index`, `Exchange` or `Custom`                              |
| `coverage_from`  | no       | First date this directory claims to cover. Inclusive         |
| `coverage_until` | no       | First date it no longer claims. Exclusive; blank = maintained |

```csv
code,name,kind,coverage_from,coverage_until
DEMO_INDEX,Demonstration Index (synthetic),Index,2026-01-02,
```

**Leaving the coverage columns blank is honest and has a consequence.** The
universe is recorded, every as-of read against it answers *unknown* rather than
*empty*, and a coverage finding is raised saying so. That is the correct state
for a set whose history nobody has sourced yet; it is not a state to fill in
with a guess.

A universe already recorded keeps its name and kind. Only the coverage claim is
refreshed on a later import, because that is the one thing an import genuinely
restates — sourcing older history widens it.

## `universe-memberships.csv`

| Column           | Required | Meaning                                                        |
| ---------------- | -------- | -------------------------------------------------------------- |
| `universe_code`  | yes      | The `code` of a universe defined in `universes.csv`             |
| `symbol`         | yes      | The source's own spelling of the symbol                         |
| `effective_from` | yes      | First date of membership. **Inclusive**                         |
| `effective_to`   | no       | First date of non-membership. **Exclusive**; blank = still a member |
| `announced_on`   | no       | When the change was published                                   |

```csv
universe_code,symbol,effective_from,effective_to,announced_on
DEMO_INDEX,FPT.HM,2026-01-02,,2025-12-15
DEMO_INDEX,VNM.HM,2026-01-02,2026-04-01,2025-12-15
DEMO_INDEX,VNM.HM,2026-07-01,,2026-06-15
```

Dates are `yyyy-MM-dd`, parsed invariantly. A date that cannot be read rejects
the file rather than the row: the difference between a security being in an
index and not is not something to guess at.

### The interval is half-open

`effective_to` is the **removal date**, not the last day held. A review that
removes one name and admits another happens on a single date, and only a
half-open interval puts that date on exactly one side of each — otherwise an
index of thirty briefly holds thirty-one.

### Re-entry is a second row

A security demoted at one review and restored at a later one has two rows with
disjoint intervals, as `VNM.HM` does above. It is never one row edited: the gap
between the spells is exactly what a survivorship-free backtest must be able to
see.

Two rows for one security whose intervals **overlap** are refused — by the
import, which names the security, and by an exclusion constraint on the table,
which is what makes the rule true regardless of who is writing.

### `announced_on` is recorded and not yet read

An index review is published before it takes effect, so a strategy acting on
the announcement earlier than its publication date would be looking ahead.
Filtering on it is U4's work. The column is collected now so the history does
not have to be re-sourced then.

## What the import does

Additive and idempotent:

| Situation | Outcome |
| --------- | ------- |
| Spell not recorded | Created |
| Spell recorded exactly as reported | Unchanged |
| Open spell the source now reports as ended | Closed |
| Spell already recorded as ended, reported with a different ending | **Rejected** |
| Spell overlapping one already recorded | **Rejected** |
| Symbol that resolves to no instrument | **Rejected** |
| Interval covering no session | **Rejected** |

Nothing is ever deleted. A source that stops publishing a spell may simply have
shortened its window, and inferring a removal from an absence would rewrite
which securities a strategy could have chosen from.

A spell already recorded as ended is a fact something may have already run a
backtest against, so a source that changes its mind about one is rejected
rather than silently rewritten. That needs a decision, not an import.

After the rows are applied, the coverage review runs **in the same
transaction** and records what is still missing — a universe with no membership
at all, one whose rows exist while it claims no span, or rows outside the span
it claims.
