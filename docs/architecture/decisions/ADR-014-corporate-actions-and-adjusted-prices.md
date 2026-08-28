# ADR-014: Corporate actions and adjusted prices

**Status:** Accepted · **Date:** 2026-08-27 · **Phase:** 4

## Context

Every price the system holds is the price that printed. That is the right thing
to store and the wrong thing to compute on. A security that splits two-for-one
halves overnight; a momentum signal reads a 50% crash, a stop-loss backtest
fires, and a five-year return series is wrong by the product of every action
that ever happened to the issuer.

Phase 3 already sees the discontinuity — a close outside HOSE's ±7% band raises
a `PriceLimitBreach` — and deliberately leaves it open, saying only that
something happened here that a single row cannot explain. This phase is the
explanation, and the open findings are its queue.

Vietnam makes the problem harder than the textbook version:

- **Rights issues are routine**, not exotic. A listed company raising capital
  offers new shares to holders below market, and the resulting adjustment
  depends on the subscription price rather than on any figure in the price
  series.
- **Bonus shares and stock dividends are everywhere.** Both increase the share
  count with no cash moving, and both are reported in local practice with the
  ratio meaning *additional* shares per share held rather than shares after per
  share before. A system that treats them as splits is wrong by exactly one.
- **Several actions routinely go ex on the same morning** — a cash dividend
  alongside a bonus issue — so the adjustment for a date is a product, not a
  single factor.
- **Actions get restated.** A ratio is announced, then corrected. Whatever the
  system does must survive the correction arriving after the series has already
  been read a hundred times.

## Decision

**Raw bars are never rewritten.** `quant.bars` holds what the source printed and
keeps holding it. Adjustment is a multiplier stored beside the series in
`quant.price_adjustments` and applied when the series is read.

This is the load-bearing decision and everything else follows from it. The
alternative — materialising an adjusted series, or worse, rewriting the bars in
place — means a corrected ratio arriving a year later has nothing to correct
*from*. Here a wrong factor is one row in a small derived table, and fixing it
is one recompute.

**Adjusted is the default on the read path.** `GET /bars` returns an adjusted
series unless asked otherwise, and the response says which it returned. The
reasoning is that unadjusted data is not a neutral choice: it is silently wrong
for almost every question anyone asks of a price series, and a default that is
silently wrong is a trap. A caller that genuinely wants the printed price —
reconciling against a broker statement, checking a limit band — asks for it and
gets it labelled.

**Eight action types, with rights issues and bonus shares first-class.**
`CashDividend`, `StockDividend`, `BonusShares`, `StockSplit`, `ReverseSplit`,
`RightsIssue`, `ShareIssuance`, `SymbolChange`. The first six rescale the
series; the last two are recorded because they are facts a later phase needs and
because an absent factor with a stated reason reads differently from an absent
factor nobody computed.

Modelling rights issues as a dividend variant was considered and rejected. Their
factor is the theoretical ex-rights price, which needs a subscription price the
dividend formula has no field for — the model would have had to carry the field
anyway, in a type whose name denied it.

**Each type carries only the amounts it uses, and a row carrying a surplus is
refused.** A cash dividend with a ratio is not a dividend with a harmless extra
field; it is a row whose author meant something else. Recording half of it would
leave a record that reads as though it means something it does not.

**A factor is two numbers, not one:** a price multiplier and a share multiplier.
A split scales prices by `1/r` and volumes by `r`; a cash dividend scales prices
and leaves volume alone. Collapsing them into one number would make adjusted
volume either wrong or unavailable, and volume is half of most liquidity work.

**Turnover is never rescaled.** Cash traded is cash traded, and it is invariant
across every action here — the price multiplier and the share multiplier are
reciprocal wherever both apply.

**The formulas**, each measured against the close of the last session *before*
the ex-date:

| Type | Price factor | Share factor |
| ---- | ------------ | ------------ |
| Cash dividend | `(P − D) / P` | 1 |
| Split, reverse split | `1 / r` | `r` |
| Stock dividend, bonus shares | `1 / (1 + r)` | `1 + r` |
| Rights issue | `TERP / P`, where `TERP = (P + r·S) / (1 + r)` | `1 + r` |

**An action with no price before it is rejected, not guessed.** Two of the four
formulas divide by the previous close. A listing's first-ever action has none,
and inventing one would silently rescale the whole series by a made-up number.
The rejection is reported per action, so one unusable row does not stop the
other nine.

**Factors are stamped with the action version and the rule version they came
from.** An amendment bumps the action's version and the stored factor no longer
matches it, which makes staleness a comparison rather than a re-derivation of
everything. Recomputing is idempotent: a factor that still describes its action
is left alone, and the run reports *unchanged* separately from *computed* so a
staleness check that has stopped working is visible.

**Recompute is triggered by the import, for the instruments it touched.** Not
for the universe, and not on a schedule. An import that created or amended
nothing needs no recompute, and says so.

**Explaining a Phase 3 finding is a resolution, not an edit.** When a recompute
produces a factor whose ex-date lands on an open `PriceLimitBreach`, the finding
is closed with the action that accounts for it. The finding keeps its history;
nothing is deleted.

**There is no write endpoint and no recompute endpoint.** Actions arrive through
the import, factors follow it, and both are driven by the host. A surface that
let a caller inject an action would let a caller rescale a decade of prices with
one request.

## Alternatives considered

**Store an adjusted series alongside the raw one.** Rejected: it doubles the
largest table in the system, and every correction rewrites a decade of rows
under a lock. The factor table is one row per action per instrument, and the
rescale is a multiplication over a page of results.

**Store a cumulative adjustment factor on each bar.** Faster to read — no join,
no walk — but every action inserts a value into every historical row, which is
the rewrite problem in a different shape. Rejected for the same reason.

**Adjust in the client.** Rejected: it puts the formulas in every consumer, and
the Python quant layer and the C++ engine would each need their own copy. The
maths that decides what a five-year return means belongs in one place.

**Unadjusted by default.** Rejected above. The considered version of this
argument is that adjusted data is derived and a store should serve what it
stored; the answer is that the store still serves it, labelled, on request.

## Trade-offs accepted

- **The adjusted read costs a second query and a walk.** Bounded by the number
  of actions on one instrument, which is small, and it buys a raw series that is
  never touched.
- **A cash dividend's factor depends on a price, so it is not reproducible from
  the action alone.** The reference close is stored with the factor for exactly
  this reason: "that factor looks wrong" is otherwise unanswerable from outside.
- **Adjusted prices drift from any round number.** Ten years and twenty actions
  leave a series whose historical prices match no printed figure anywhere. That
  is inherent to adjustment, and the raw series remains available to reconcile
  against.
- **Nothing verifies an imported action against the price series.** A ratio
  transcribed as 20 instead of 2 produces a plausible-looking factor and a
  ruined chart. Phase 3's band check catches the *unadjusted* discontinuity, not
  a wrong adjustment of it; a cross-check belongs with the screener work.
- **Intraday series are adjusted by the same daily factors.** Correct — an
  action's effect is a level shift, not a resolution-dependent one — but the
  ex-date boundary is applied at midnight UTC, which for a Vietnamese session
  falls mid-morning local. Daily series are unaffected; a five-minute series
  spanning an ex-date is approximated by less than one session.
- **Corporate action history has to be transcribed by hand.** There is no free
  API for it in this market. The file source is the reference implementation
  because that is genuinely how the data arrives.

## Consequences

- A backtest over a window containing a split now reads a continuous series, and
  the discontinuity Phase 3 recorded has an answer attached to it rather than
  staying open forever.
- Phase 5's technical analysis operates on the adjusted series by default, so a
  moving average does not jump on an ex-date.
- Phase 9's point-in-time guarantee has the field it needs: `announced_on` is
  stored, so a look-ahead check can ask whether the market knew yet.
- A deployment that imports no actions gets a series adjusted by nothing, which
  reads exactly like a correctly adjusted one. That is the phase's remaining
  sharp edge, and the honest mitigation is the same as Phase 3's: the absence is
  visible — no actions listed, no factors on the bars — rather than papered over
  with a guess.
