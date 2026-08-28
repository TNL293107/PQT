# Corporate action CSV

The interchange format the file-backed corporate action source reads. Like the
market data source it is a real provider from the pipeline's point of view:
the same import, the same rejection reasons, the same audit trail as an HTTP
source would get.

It exists as a file source because that is how this data actually arrives.
Vietnamese corporate action history is published as exchange disclosures, one
announcement at a time, rather than served from an API — every system that
needs it ends up transcribing it into a table.

Configure it by pointing `MarketData__CorporateActionPath` at a single file.
With that setting blank, no source is registered and nothing is imported — see
`.env.example`.

## Columns

A header row is required. Columns are matched **by name**, not by position, so
a reordered export is read correctly rather than silently transposed.

| Column          | Required | Meaning                                                |
| --------------- | -------- | ------------------------------------------------------ |
| `symbol`        | yes      | The source's own spelling of the symbol                 |
| `type`          | yes      | What the issuer did — see the table below               |
| `ex_date`       | yes      | First date the security trades **without** the benefit  |
| `ratio`         | depends  | Meaning depends on the type                             |
| `cash_amount`   | depends  | Meaning depends on the type                             |
| `record_date`   | no       | When the register closes                                |
| `payment_date`  | no       | When cash or shares arrive                              |
| `announced_on`  | no       | When the action became public                           |

```csv
symbol,type,ex_date,ratio,cash_amount,record_date,payment_date,announced_on
DEMO,CashDividend,2026-08-10,,350,2026-08-11,2026-08-25,2026-07-20
DEMO,StockSplit,2026-09-14,2,,2026-09-15,,2026-08-30
```

Dates are `yyyy-MM-dd`. Numbers use `.` as the decimal separator and no
thousands separator, and both are parsed invariantly, so the same file means
the same actions on every machine. That matters more here than anywhere else
in the system: a ratio read with the wrong decimal separator rescales a decade
of prices by a factor of a thousand.

## Types

`type` is matched case-insensitively against the names below. A row whose type
is missing or unrecognised is rejected — never recorded as "some other kind of
event", which would leave a row in the system of record that nothing can act
on.

| `type`          | `ratio` means                              | `cash_amount` means      | Rescales prices |
| --------------- | ------------------------------------------ | ------------------------ | --------------- |
| `CashDividend`  | —                                          | Cash per share           | yes             |
| `StockDividend` | Additional shares per share held           | —                        | yes             |
| `BonusShares`   | Additional shares per share held           | —                        | yes             |
| `StockSplit`    | Shares after per share before              | —                        | yes             |
| `ReverseSplit`  | Shares after per share before (below 1)    | —                        | yes             |
| `RightsIssue`   | New shares offered per share held          | Subscription price       | yes             |
| `ShareIssuance` | —                                          | —                        | no              |
| `SymbolChange`  | —                                          | —                        | no              |

**The ratio means a different quantity for each type**, so a row's ratio can
only be read together with its type. A `StockSplit` of `2` doubles the share
count; a `StockDividend` of `2` triples it, because two new shares arrive for
every one held.

A row carrying an amount its type does not use is rejected rather than having
the surplus ignored. `CashDividend,2026-08-10,2,350` is not a dividend with a
harmless extra field — it is a row whose author meant something the format
cannot express, and recording half of it would be worse than recording none.

The last two types are declared but rescale nothing. They are recorded because
they are facts about the issuer that a later phase needs — a symbol change is
how a ticker gets reused, a share issuance changes the denominator under every
per-share figure — and because a factor that is absent for a stated reason
reads differently from one that is absent because nobody looked.

## Duplicates and re-imports

An action is identified by **instrument, type and ex-date**. Re-importing a
file changes nothing: rows already held are counted as unchanged, and only a
row whose ratio, cash amount or dates have moved is recorded as an amendment.

The same key twice **within one file** is rejected as a duplicate rather than
applied twice. Two rows for the same instrument, type and ex-date are a
transcription error, and the second one silently amending the first would hide
it.

An amendment bumps the action's version, which invalidates any adjustment
factor computed from the earlier one. The import triggers a recompute for the
instruments it touched, so a corrected ratio reaches the series without anyone
having to remember to ask.

## How a symbol is resolved

The symbol is matched against the **provider symbol alias** the instrument
import recorded for the same source, exactly. There is deliberately no
fallback to the bare ticker: a ticker is live on one venue at a time but
reused across them and across delistings, so a fallback would eventually
attach a dividend to the wrong company. A row whose symbol resolves to nothing
is rejected as `UnknownInstrument`.

The practical consequence is that the instrument import must run first, from
the same source code, or every row is refused.

## What happens after the import

Recording the action is half the work. The other half is the factor the series
is read through, which is computed separately and stored beside the raw bars —
see [ADR-014](../../docs/architecture/decisions/ADR-014-corporate-actions-and-adjusted-prices.md).

Raw bars are never rewritten. A factor computed from a wrong ratio is a wrong
row in a small derived table, and correcting it is one recompute; a rewritten
price series has no way back.

## Rejection reasons

| Reason                 | What it means                                        |
| ---------------------- | ---------------------------------------------------- |
| `UnknownInstrument`    | The symbol matched no provider alias for this source  |
| `UnknownType`          | The type was missing or is not one recorded here      |
| `InconsistentAmounts`  | The ratio or cash amount contradicted the type        |
| `InconsistentDates`    | A date contradicted the ex-date                       |
| `DuplicateWithinImport`| The same key appeared twice in one file               |

Rejections are per row. One dividend transcribed in the wrong unit does not
stop the other nine actions in the file from being recorded.

An unreadable date or number is different: it fails the **whole file** rather
than one row, because a parse that cannot read one ratio has no grounds to
claim it read the next one correctly.

## Fixture

[`../fixtures/corporate-actions.csv`](../fixtures/corporate-actions.csv) is a
tiny synthetic file for the `DEMO` series. Every figure in it was invented.
