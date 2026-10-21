# Corporate actions of the eight names with none recorded (sprint 8, item 8.4)

BCM, MSN, MWG, PLX, POW, VNM, VPL and VRE had no corporate action at all in
`corporate-actions.csv`. The sprint-5 rows were sourced to explain price-band
breaches, and none of these eight breached. This is a **sweep**, not a breach
search: every entitlement notice VSDC lists for each name is read, and every
one that changes a price or a share count is transcribed.

## Method

- **Source.** VSDC's entitlement calendar ("Lịch thực hiện quyền",
  `vsdc.vn/vi/lich-giao-dich?tab=LICH_THQ`). It was searched by stock code over
  record dates 01/04/2023 → 31/12/2026, read on 2026-10-21, 10 rows a page,
  3 seconds apart. The page's own search request (`POST /lich-thq/search`,
  with the `__VPToken` the page carries) was used, as the page itself makes
  it.
- **Filter.** Only rows whose code is the ticker itself are kept. The search
  also returns covered warrants such as `CVNM2211`.
- **Notices read.** Every notice that names a dividend, a rights issue, a
  stock dividend or a bonus issue was opened and read: 24 notices for the
  eight names. Meeting notices, written-ballot notices and warrant notices
  carry no entitlement and are listed below only as counts.
- **Fields.**
  - `ex_date` is the HOSE session before the record date, by
    `trading-calendar.csv`. Settlement is T+2, as in sprint 5.
  - `announced_on` is the notice's "Cập nhật ngày" date. This is the
    fixture's convention: the latest evidenced date, not the issuer's first
    disclosure.
  - `cash_amount` is đồng per share, and `payment_date` is the notice's
    payment date.
- **Coverage claim.** The search covers record dates from 01/04/2023, so every
  action with an ex-date on or after 2023-07-01 is in it. The notices list no
  record date after 08/10/2026 for these names, read on 2026-10-21.

## BCM — Becamex IDC

11 notices: 3 cash dividends; the rest are meetings and written ballots.

- **2022 cash dividend.** 8%, 800 ₫/share. Record date 29/11/2023, paid
  29/12/2023. Notice dated 10/11/2023: https://vsdc.vn/vi/ad/164326
  - The 05/02/2024 cancellation (https://vsdc.vn/vi/ad/167347) cancels only
    the written-ballot list of that record date, not the dividend.
- **2023 cash dividend.** 10%, 1,000 ₫/share. Record date 05/11/2024, paid
  27/12/2024. Notice dated 17/10/2024: https://vsdc.vn/vi/ad/175904
- **2024 cash dividend.** 11%, 1,100 ₫/share. Record date 13/02/2026, paid
  13/03/2026. Notice dated 03/02/2026: https://vsdc.vn/vi/ad/191571

## MSN — Masan Group

5 notices, all meetings or written ballots (2023-07, 2024-03, 2024-09,
2025-03, 2026-03). **No entitlement** from 2023-07-01 to 2026-10-21.

## MWG — Mobile World

8 notices: 5 cash dividends; the rest are meetings.

- **2022 cash dividend.** 5%, 500 ₫. Record 28/07/2023, paid 10/08/2023.
  Notice 05/07/2023: https://vsdc.vn/vi/ad/159842
- **2023 cash dividend.** 5%, 500 ₫. Record 01/07/2024, paid 10/07/2024.
  Notice 14/06/2024: https://vsdc.vn/vi/ad/172036
- **2024 cash payment.** 10%, 1,000 ₫. Record 25/07/2025, paid 08/08/2025.
  Notice 26/06/2025: https://vsdc.vn/vi/ad/184399
- **2025 first cash dividend.** 10%, 1,000 ₫. Record 30/07/2026, paid
  06/08/2026. Notice 20/07/2026: https://vsdc.vn/vi/ad/198355
- **2025 second cash dividend.** 10%, 1,000 ₫. Record 08/10/2026, paid
  16/10/2026. Notice 22/09/2026: https://vsdc.vn/vi/ad/200488

## PLX — Petrolimex

9 notices: 4 cash dividends; the rest are meetings.

- **2022 cash dividend.** 7%, 700 ₫. Record 22/09/2023, paid 10/10/2023.
  Notice 14/09/2023: https://vsdc.vn/vi/ad/161642
- **2023 cash dividend.** 15%, 1,500 ₫. Record 15/05/2024, paid 28/05/2024.
  Notice 07/05/2024: https://vsdc.vn/vi/ad/170705
- **2024 cash dividend.** 12%, 1,200 ₫. Record 11/06/2025, paid 24/06/2025.
  Notice 03/06/2025: https://vsdc.vn/vi/ad/183549
- **2025 cash dividend.** 12%, 1,200 ₫. Record 05/08/2026, paid 20/08/2026.
  Notice 23/07/2026: https://vsdc.vn/vi/ad/198519

## POW — PV Power

5 notices: 1 entitlement notice with three actions on one record date; the
rest are meetings.

- **Record 11/12/2025.** Notice 02/12/2025: https://vsdc.vn/vi/ad/189687
  - **Rights issue.** 100:12 at 10,000 ₫. 281,024,592 shares expected.
    Rights traded 18/12–29/12/2025; subscription 18/12/2025–06/01/2026.
  - **Stock dividend.** 100:4.
  - **Issue from owners' equity.** 100:15, recorded as `BonusShares` (the
    fixture's convention, see the README).

## VNM — Vinamilk

10 notices: 8 cash dividends; the rest are meetings.

- **Record 04/08/2023.** 24.5%, 2,450 ₫: the rest of 2022 (9.5%) plus the
  first 2023 interim (15%). Paid 05/10/2023. Notice 17/07/2023:
  https://vsdc.vn/vi/ad/160128
- **Record 28/12/2023.** Second 2023 interim, 5%, 500 ₫. Paid 28/02/2024.
  Notice 13/12/2023: https://vsdc.vn/vi/ad/165609
- **Record 18/03/2024.** Third 2023 interim, 9%, 900 ₫. Paid 26/04/2024. The
  same notice calls the 2024 AGM. Notice 07/03/2024:
  https://vsdc.vn/vi/ad/168521
- **Record 25/09/2024.** 24.5%, 2,450 ₫: final 2023 (950 ₫) plus the first
  2024 interim (1,500 ₫). Paid 24/10/2024. Notice 30/08/2024:
  https://vsdc.vn/vi/ad/174349
- **Record 27/12/2024.** Second 2024 interim, 5%, 500 ₫. Paid 28/02/2025.
  Notice 10/12/2024: https://vsdc.vn/vi/ad/177392
- **Record 15/05/2025.** Rest of 2024 (first payment), 20%, 2,000 ₫. Paid
  23/05/2025. Notice 29/04/2025: https://vsdc.vn/vi/ad/182377
- **Record 17/10/2025.** 28.5%, 2,850 ₫: rest of 2024 (350 ₫) plus the first
  2025 interim (2,500 ₫). Paid 24/10/2025. Notice 02/10/2025:
  https://vsdc.vn/vi/ad/187729
- **Record 29/06/2026.** Rest of 2025, 18.5%, 1,850 ₫. Paid 17/07/2026.
  Notice 17/06/2026: https://vsdc.vn/vi/ad/197038

## VPL — Vinpearl

3 notices. VPL listed on HOSE on 2025-05-13; its coverage begins there.

- **Not transcribed: rights issue, record 10/01/2025.** 1,000:40.673 at
  71,350 ₫ (notice 02/01/2025, https://vsdc.vn/vi/ad/178215). It precedes the
  listing, so no listed price or session is affected.
- The other two notices are the 2025 and 2026 AGMs. **No entitlement** from
  the listing to 2026-10-21.

## VRE — Vincom Retail

5 notices: 1 cash dividend; the rest are meetings or written ballots.

- **2025 cash dividend.** 10%, 1,000 ₫. Record 01/07/2026, paid 22/07/2026.
  The same notice runs a written ballot. Notice 19/06/2026:
  https://vsdc.vn/vi/ad/197098

## Price check

**Cash dividends.** The close on each ex-date was read against the session
before it. A cash dividend of 1–3% cannot be confirmed from one move against
market noise, so this check only rules out a gross misdating. Every ex-date
with a bar moved by less than the HOSE band. The two largest drops were VNM
2023-08-03 (−4.6%, against a 2,450 ₫ dividend on 76,100 ₫) and PLX 2024-05-14
(−5.9%, against 1,500 ₫ on 39,850 ₫).

**No bar on the ex-date for three cases.** No bar is stored for BCM on
2026-02-12, for PLX on 2026-08-04, or for POW on 2025-12-10. Each stored series
ends when the name left VN30:

| name | last stored bar | VN30 spell ends |
| --- | --- | --- |
| BCM | 2026-01-28 | 2026-02-02 |
| PLX | 2026-07-31 | 2026-08-03 |
| POW | 2025-01-24 | 2025-02-03 |

No stored bar exercises these three adjustments, so the rows stand on the
notices alone.
