# Evidence: Sprint 8.1, the "Capital stepped" findings

Checked 2026-10-21 by QA + QC, read-only. Source: dataset `e1d22bee4937dad9`
v1 `fundamentals.parquet`, the 356 rows whose `findings` contain "Capital
stepped", grouped by instrument and finding text. Instrument ids were mapped
to tickers through `quant.instrument_identifiers`. Notices were found by web
search and read on vsdc.vn where a notice id is given.

The dataset reports 7 distinct finding texts. HPG 2024Q1→Q2 appears with two
Q2 capitals (63,962,642,700,000 for 19 rows, then 63,962,502,000,000 for 103
rows, a restated Q2), so this is **6 name–step pairs**. ACB 2023Q1→Q2 is not
among them: the dataset starts on 2024-08-05, after that balance sheet stopped
being the latest. It is listed last because the 8.1 evidence cites it.

## Pairs

| name | quarters | step | rows | notice | verdict |
| --- | --- | --- | --- | --- | --- |
| HPG | 2024Q1 → 2024Q2 | +10.0% | 122 | VSDC https://vsdc.vn/vi/ad/170903: capital raised from equity (bonus), 10:1, record 24/05/2024, ex 23/05/2024 | **missing-action**: no HPG 2024 row in `corporate-actions.csv` |
| HPG | 2025Q2 → 2025Q3 | +20.0% | 45 | VSDC https://vsdc.vn/vi/ad/183533: 2024 dividend in shares, 10:2, record 27/06/2025, ex 26/06/2025 | **detector-window**: the action is recorded (ex 2025-06-26), but the shares reached the Q3 balance sheet |
| TPB | 2024Q3 → 2024Q4 | +20.0% | 60 | 2023 dividend in shares, 100:20, record 24/09/2024 (press; no VSDC id found) | **detector-window**: the action is recorded (ex 2024-09-23), but the shares reached the Q4 balance sheet |
| MSN | 2024Q2 → 2024Q3 | +5.2% | 65 | none found for the whole step. Press: a 7.5M ESOP took capital 15,054 → 15,129 bn; nothing found for 14,384 → 15,054 bn (≈67.1M shares) | **real-finding** (probably not pro rata), cause unconfirmed |
| VHM | 2024Q1 → 2024Q4 | −5.7% | 23 | press: 247M treasury shares bought back 23/10–21/11/2024 and cancelled, capital 43,544 → 41,074 bn from 03/12/2024. Not a VSDC entitlement | **real-finding**: a buyback and cancellation, not an entitlement |
| VJC | 2025Q1 → 2025Q2 | +9.2% | 41 | press: 50M-share private placement at 100,000 ₫ to two Sovico-linked buyers, closed 12/06/2025. Not a VSDC entitlement | **real-finding**: a placement, not pro rata |
| ACB (outside the dataset) | 2023Q1 → 2023Q2 | +15.0% | — | VSDC https://vsdc.vn/vi/ad/159023: 2022 dividend, 100:15 in shares plus 10% cash, record 02/06/2023, ex 01/06/2023 | **missing-action**: `corporate-actions.csv` has ACB only from 2024-05 |

## What this means for 8.1

- **Two rows for Dev (missing-action)**: ACB StockDividend 0.15 plus CashDividend
  1,000, ex 2023-06-01, record 2023-06-02, announced 2023-05-23 (the notice
  date), paid 2023-06-12. HPG bonus 0.10, ex 2024-05-23, record 2024-05-24,
  announced 2024-05-10 (the notice date).
- **A detector problem (detector-window, 105 rows)**: `CheckUnexplainedStep`
  (`FundamentalRatioService.cs:544-549`) only counts an action whose ex-date
  falls in (prior quarter end, balance quarter end]. Vietnamese stock
  dividends are issued 2–5 weeks after the ex-date, so an ex-date in the last
  weeks of a quarter shows up in the next quarter's charter capital, and the
  recorded action is missed. HPG 2025 and TPB 2024 both fail this way. Widening
  the window (for example an ex-date up to about 60 days before the prior
  quarter end) is a code change, so it goes to Lead and Dev and is not made
  here.
  The same lag may also affect how `SharesAt` applies these actions in those
  weeks. I did not check that.
- **Real findings (3 pairs, 129 rows)**: MSN, VHM and VJC are capital changes
  that are not pro rata (placement, ESOP or conversion, cancellation). This is
  the finding working as designed. The 8.1 evidence named MSN as a real case,
  and that verdict stands, although what caused the step is still unconfirmed.
  The 8.1 evidence uses ACB as its example of a real case, and ACB should be
  replaced as that example.
