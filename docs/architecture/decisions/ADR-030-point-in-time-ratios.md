# ADR-030: Point-in-time ratios and the share count behind them

**Status:** Accepted · **Date:** 2026-10-18 · **Phase:** Sprint 7, Phase 6 (point-in-time fundamentals)

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
   announced later is not counted, as in a strict dataset read.
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
  overstated, by the treasury fraction. For VN30 names that fraction is
  small. The bias is one-directional and documented, and it is not hidden by
  an estimate.
- **A rights issue's share factor assumes full subscription.** That is how
  the adjustment engine already states it (ADR-014).
- **Charter capital moves only when registered.** An action's shares are
  counted from its ex-date. If registration falls after the next quarter
  end, that quarter's charter capital may not include them yet, and both
  counts could then agree or differ by the action. This is accepted: the
  action is counted from the first balance sheet dated after its ex-date,
  and never twice, because only actions after the balance-sheet quarter
  are applied.

## Consequences

- Ratios are computed on read by `IFundamentalRatioService`, not stored.
  They follow every restatement automatically.
- Each ratio carries the report, the quarters and the actions it used, so a
  surface (7.2) can say where a number came from.
- The dataset contract (7.3) will carry the same values as of each session,
  through the canonical export rather than a side channel.
