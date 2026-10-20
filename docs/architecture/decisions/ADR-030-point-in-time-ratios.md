# ADR-030: Point-in-time ratios and the share count behind them

**Status:** Proposed · **Date:** 2026-10-18 · **Phase:** Sprint 7, Phase 6 (point-in-time fundamentals)

## Context

Sprint 7 item 7.1 asks for EPS over the trailing twelve months, P/E, P/B,
ROE and dividend yield, each from the statements known at an instant and a
share count adjusted for corporate actions to that instant.

ADR-029's fourth verification left the share count open. No statement form
carries shares outstanding:

- Charter capital (Circular 200 code 411a, bank code 411), divided by the
  ₫10,000 par, counts every common share issued, treasury shares included.
- Treasury shares (415, 414) are carried at cost, not as a count.

A per-share ratio built on a count from the wrong instant is wrong by the
action in between. A 20% bonus issue between the balance-sheet date and the
day a ratio is read cuts every per-share figure by a sixth.

PQT already holds what is needed to bridge that gap. Each corporate action
becomes a `PriceAdjustment` with a share factor, the multiplier that turns a
pre-ex-date share count into a post-ex-date one, and with the date the action
was announced (ADR-022).

## Decision

**Every ratio is read at a session `t`, from the statements known before `t`
opened (ADR-029's as-of read), the raw close of `t`, and actions announced
by `t`.** A ratio that cannot be computed honestly is absent, with a reason.
It is never zero and never estimated.

1. **Shares at `t`** are the charter capital of the latest known balance
   sheet, divided by ₫10,000. That figure is multiplied by the share factor
   of every action that went ex after the balance sheet's quarter ended and
   on or before `t`, and that was announced on or before `t`. An action
   announced later is not counted, as in a strict dataset read. The two
   checks below may add an action registered late or withhold one
   registered early, and say so.
2. **Net profit to the parent over the trailing twelve months** is the sum of
   the latest four quarters known, each read with the `Quarter` span: Circular
   200 code 61, or bank code 21 less 22. The four must be consecutive. A
   missing quarter makes the trailing figure absent, not three quarters long.
3. **EPS (TTM)** is that profit divided by shares at `t`.
4. **Book value per share** is equity to the parent from the latest known
   balance sheet, divided by shares at `t`. On Circular 200 that is code 400
   less 429; on the bank form it is code 500.
5. **P/E** is the close at `t` over EPS. It is absent when EPS is zero or
   negative, because a loss has no meaningful earnings multiple. EPS itself
   is still shown.
6. **P/B** is the close at `t` over book value per share.
7. **ROE (TTM)** is the trailing profit over the average of equity to the
   parent at the latest quarter end and four quarters earlier. With only the
   latest balance known, ROE is absent rather than computed on one end.
8. **Dividend yield (TTM)** is the cash dividends per share that went ex in
   the 365 days to `t` and were announced by `t`. Each dividend is divided by
   the share factors of actions between its ex-date and `t`, so it is stated
   on today's share basis. The sum is divided by the close at `t`.

The close is the raw close of session `t`. The share count is on the basis
of `t`, so the close must be too. An adjusted close would mix bases.

The two cuts differ on purpose:
- Statements and actions are cut at 00:00 UTC of `t`, which is 07:00 in
  Hanoi, before the session opens.
- The close is the end of `t`.

A ratio is therefore what an investor could compute after the session closed,
using only what was published before it opened. Nothing published during the
session is used.

The fiscal year is taken to end in December, as it does for every name in
the first import (ADR-029). A trailing year is four calendar quarters.

**Charter capital** is read as follows:
- Bank form: 411.
- Circular 200: common shares (411a) when they and preferred shares (411b)
  add up to the total (411). MSN and VIC hold preferred shares, which a
  per-share ratio must not count.
- Circular 200, when the split does not add up and there are no preferred
  shares: 411. VNM's 2025 forms carry a stray 411a.
- Otherwise the count is absent.

**A cancelled action counts nowhere, at any session.** No cancellation date
is recorded. An action called off before its ex-date never changed a share.
One "cancelled" after its ex-date is a correction of the record. Only actions
that went ex by `t` are read, so excluding it borrows nothing from the
future.

**Dividend yield is zero only where zero is known.** It needs the issuer's
recorded corporate-action history to reach back over the whole trailing year,
meaning its earliest recorded action is at least 365 days before `t`.
Otherwise the yield is absent (`NoDividendCoverage`). A history that starts
inside the year cannot tell "paid nothing" from "not recorded".

**Every absence carries a code** (`RatioAbsence`) and a sentence. A ratio
built on an absent one carries its code: P/E on a missing quarter is
`MissingQuarter`, not "no earnings". The codes are:
- `NoBalanceSheet`
- `NoCharterCapital`
- `NoQuarterlyProfit`
- `MissingQuarter`
- `NoClose`
- `TrailingLoss`
- `EquityNotPositive`
- `EquityNotKnownAtBothEnds`
- `NoDividendCoverage`

## The two checks, as implemented (sprint 7 item B, 2026-10-20)

Both report **findings**: sentences returned with the ratios. A check never
estimates a figure. It applies an action, withholds one, or says what it saw.

**Charter capital against share factors.** Every announced share action that
went ex by `t` is compared with the step in capital around it. Two quantities
are compared:
- the observed step: capital at the later balance sheet divided by capital at
  the earlier one;
- the expected step: the product of the factors of the announced actions
  between the two balance sheets.

The two match when they differ by less than 2%, or by less than half of
`|factor − 1|` when that is smaller. The check has three outcomes:
- *Registered late.* The action went ex within the year to the balance sheet,
  and the step matches the expected step without this action's factor. It is
  applied, with a finding.
- *Registered early.* The action goes ex after the balance sheet. The
  balance sheet's step from the quarter before already matches this action's
  factor, and the actions in that quarter do not explain it. The action is
  not applied a second time, with a finding.
- *Unexplained.* The step matches neither. The finding states both numbers
  and changes nothing. A step with no action near it at all, such as ACB's 15%
  rise in 2023Q2, is not checked, because nothing is applied on it.

**Par against reported EPS.** For the latest trailing quarter that reports
basic EPS (70, bank 23), the check divides the quarter's profit by
capital ÷ ₫10,000. Reported EPS deducts the bonus and welfare fund and is
weighted over the period, so it runs below that figure by up to a sixth. A
result more than twice or less than half the reported EPS is a finding: the
share count or a unit is in doubt.

## Alternatives

- **Sum the reported quarterly EPS.** EPS is on the form (code 70, bank 23),
  but each quarter's EPS is on that quarter's weighted share count. Summing
  four of them across a bonus issue mixes four bases. It is also blank on
  many quarterly forms.
- **Listed shares from HOSE.** HOSE sells this as a paid statistic (ADR-029
  verification 1), which the free constraint excludes.
- **Infer the count from profit and reported EPS.** It is a weighted average
  over the period, not a count at an instant, and it inherits the blanks.

## Trade-offs

- **Treasury shares are counted as outstanding.** Where an issuer holds
  treasury shares, its per-share figures are understated and its P/E and P/B
  overstated, by the treasury fraction. The bias is one-directional and
  documented, and it is not hidden by an estimate.

  Measured 2026-10-18 on the ten names imported so far: treasury shares at
  cost (415, 414) are zero for nine of them, and 0.011% of charter capital
  for SHB. Cost overstates the count fraction, since treasury shares are
  bought above par. VNM, MWG and HPG, which are known to hold treasury
  shares, were not imported yet. The claim that the fraction is small is
  therefore measured for those ten names and **assumed** for the rest,
  until they are measured.
- **A rights issue's share factor assumes full subscription.** That is how
  the adjustment engine already states it (ADR-014).
- **Charter capital moves only when the increase is registered.** That
  registration can lag the ex-date. Take a bonus issue that goes ex in
  quarter Q and is registered in Q+1:
  - quarter Q's balance sheet does not include the new shares;
  - the action is not applied either, because its ex-date falls before Q
    ended;
  - so until Q+1's balance sheet is known, the count misses the action, and
    P/E and P/B are understated by it.

  Measured 2026-10-18 on the eight share-count actions of FPT, ACB and MBB
  between 2023 and 2025, each one's quarter-end capital already includes it.
  FPT's bonus that went ex on 5 July 2023 took capital from ₫11,043bn at
  2023Q2 to ₫12,700bn at 2023Q3, which is 1.15×. The case is possible, so
  the implementation must check for it:
  - for every share-count action, compare the charter capital of the two
    balance sheets around its ex-date with its share factor;
  - when the step is missing in the quarter of the ex-date, apply the
    action in that quarter as well, and record a finding;
  - when the step appears twice, refuse to apply it, so that no action is
    counted twice.

  The same comparison found two other things for the backlog. ACB's capital
  rose 15% in 2023Q2 with no corporate action recorded, because the action
  predates the entitlements sourced in sprint 5. MBB's form states two
  different capital figures for 2022Q4 and for 2023Q4, in different reports.
- **Par is assumed to be ₫10,000.** It is the par of every common share
  listed in Vietnam today. The implementation checks the assumption for each
  issuer that reports basic EPS: profit to the parent divided by shares
  should land near the reported EPS. A name that misses by more than a bonus
  issue's worth gets a finding, not a silent ratio.

## Consequences

- Ratios are computed on read by `IFundamentalRatioService`, not stored.
  They follow every restatement automatically.
- Each ratio carries the report, the quarters and the actions it used, so a
  surface (7.2) can say where a number came from.
- The dataset contract (7.3) will carry the same values as of each session,
  through the canonical export rather than a side channel.
