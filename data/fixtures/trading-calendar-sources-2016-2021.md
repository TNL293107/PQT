# Vietnamese exchange closures 2016–2021: sources

Every date in `trading-calendar-2016-2021.csv` comes from **two primary exchange
notices**: the HOSE annual holiday notice and the HNX annual holiday notice.
HNX runs UPCoM, so its notices cover UPCoM as well. For most events HNX also
issued a separate notice shortly before the event, and those agree with the
annual notices. No date was taken from price data.

The CSV has no header. It holds 63 closed weekday sessions × 3 venues = 189
rows, sorted by date and then HOSE, HNX, UPCOM.

## Source key

| Key | Document | URL |
|---|---|---|
| H16 | HOSE 613/TB-SGDHCM, 03/06/2015, 2016 schedule | https://api.hsx.vn/n/api/v1/1/news/1153727 (also https://api.hsx.vn/n/api/v1/1/news/1148718); English: https://en.vietstock.vn/2015/06/hose-notice-of-holiday-schedule-2016-36-207307.htm |
| H17 | HOSE 1241/TB-SGDHCM, 07/12/2016, 2017 schedule (replaces 625/TB-SGDHCM of 26/05/2016) | https://static2.vietstock.vn/vietstock/2016/12/8/20161208_20161208%20Cong%20bo%20lich%20nghi%20giao%20dich%202017.pdf ; https://api.hsx.vn/n/api/v1/1/news/1044210 |
| H18 | HOSE 665/TB-SGDHCM, 31/05/2017, 2018 schedule | https://staticfile.hsx.vn/Uploads/News/726b11dedaec4b6abd325018efdc9e5c/20180206_Thông%20báo%20lịch%20nghỉ%20giao%20dịch%202018.pdf ; https://api.hsx.vn/n/api/v1/1/news/1057536 |
| H19 | HOSE 1108/TB-SGDHCM, 29/08/2018, 2019 schedule (this notice also closes 31/12/2018) | https://staticfile.hsx.vn/Uploads/News/3c913df51a3e41f89f50cc2ddc88660f/20180829_20180829_Cong%20bo%20lich%20nghi%20giao%20dich%202019.pdf ; English: https://en.vietstock.vn/2018/08/notice-of-holiday-schedule-2019-36-333345.htm |
| H20 | HOSE 1339/TB-SGDHCM, 07/11/2019, 2020 schedule | https://staticfile.hsx.vn/Uploads/News/71e2a457d65f49eda8a0fbdc0d5e1bd0/20191107_20191107%20-Thong%20bao%20lich%20nghi%20giao%20dich%20nam%202020.pdf ; https://api.hsx.vn/n/api/v1/1/news/1135278 |
| H21 | HOSE notice of the 2021 holiday schedule, published 15/12/2020 | https://en.vietstock.vn/2020/12/hose-notice-of-holiday-schedule-2021-36-430999.htm ; HOSE item https://api.hsx.vn/n/api/v1/1/news/1621219 (the attachment is not exposed) |
| N16 | HNX annual 2016 schedule, 01/06/2015 (UBCK 2648/UBCK-PTTT) | https://www.hnx.vn/ket-qua-tim-kiem/chi-tiet-tin-194376-0.html |
| N17 | HNX annual 2017 schedule, 26/05/2016, Tết later re-issued 14/12/2016 | https://www.hnx.vn/ket-qua-tim-kiem/chi-tiet-tin-220651-0.html ; Tết: https://www.hnx.vn/ket-qua-tim-kiem/chi-tiet-tin-237114-0.html |
| N18 | HNX annual 2018 schedule, 06/06/2017 (UBCK 3232/UBCK-PTTT) | https://www.hnx.vn/vi-vn/chi-tiet-lich-nghi-gd-60000674.html |
| N19 | HNX annual 2019 schedule, 21/08/2018 (VPCP 6519/VPCP-KGVX, UBCK 3329/UBCK-PTTT) | https://www.hnx.vn/vi-vn/chi-tiet-lich-nghi-gd-60002874.html |
| N20 | HNX annual 2020 schedule, 06/11/2019 (UBCK 3270 and 6643/UBCK-PTTT) | https://www.hnx.vn/ket-qua-tim-kiem/chi-tiet-tin-60006486-0.html |
| N21 | HNX annual 2021 schedule, 04/12/2020 (VPCP 9895/VPCP-KGVX, UBCK 3467/UBCK-PTTT) | https://www.hnx.vn/vi-vn/chi-tiet-lich-nghi-gd-60010564.html |

HNX also issued a notice before each event. Each one below repeats the same
dates. All are at `https://www.hnx.vn/ket-qua-tim-kiem/chi-tiet-tin-<id>.html`:

- 2016: New Year 207867-0; Tết 210433-0; Hùng Kings, 30/4 and 1/5 215295-0; National Day 229785-0.
- 2017: Tết 237114-0; 30/4 and 1/5 251727-1 (PDF only); National Day 262627-0 and 60001094-0.
- 2018: New Year 269414-0; Tết 273021-0; Hùng Kings, 30/4 and 1/5 282312-0; National Day 60002892-0.
- 2018/19 New Year (31/12/2018 and 01/01/2019): 60003438-0.
- 2019: Tết 60003629-0; Hùng Kings, 30/4 and 1/5 60004039-0; National Day 60005857-1.
- 2020: New Year 60007028-1; Tết 60007288-1; Hùng Kings 60007935-1; 30/4 and 1/5 60008208-1; National Day 60009447-1.
- 2021: New Year 60010797-0; Tết 60011068-0; Hùng Kings 60011563-0; National Day 60012732-1.

## Naming conventions applied

- **Name strings** are the ones the repo already uses: `New Year's Day`,
  `Tet`, `Hung Kings Commemoration`, `Reunification Day and Labour Day`,
  `National Day`, each with an optional ` (observed)`.
- **`(observed)` marks a compensatory day only.** It goes on a date the notice
  calls "nghỉ bù" because the holiday fell on a weekend. It is never used for
  the holiday itself, nor for a government-swapped day, which follows the
  repo's 2024-04-29 and 2026-01-02 rows.
  - This means a mixed block carries mixed names. For example, 2017-05-01 is
    the actual Labour Day and 2017-05-02 is observed.
  - The repo's 2023 rows label the actual Labour Day 2023-05-01 as observed,
    and its 2023-09-04 compensatory day as plain `National Day`. The repo is
    therefore not internally consistent here. Rename if you prefer the block
    style.
- **`National Day (observed)`** (2017-09-04, 2018-09-03) is a string the repo
  does not yet contain. It follows the standalone-observed pattern of
  `New Year's Day (observed)`.
- **Tết compensatory days** stay `Tet`, as in the repo.
- **Make-up Saturdays are not recorded.** These are 05/01/2019 and 04/05/2019;
  the HNX notices state explicitly that there was no trading on them.

## 2016 — 10 closed sessions; Tết Mon 8 – Fri 12 Feb

| Date | Name | Sources |
|---|---|---|
| 2016-01-01 Fri | New Year's Day | H16, N16, HNX 207867 |
| 2016-02-08 – 02-12 (Mon–Fri) | Tet | H16, N16, HNX 210433 |
| 2016-04-18 Mon | Hung Kings Commemoration (observed); 10/3 lunar fell on Sat 16/4 | H16, N16, HNX 215295 |
| 2016-05-02 Mon, 05-03 Tue | Reunification Day and Labour Day (observed) | H16, N16, HNX 215295 |
| 2016-09-02 Fri | National Day | H16, N16, HNX 229785 |

The government's 9-day civil-service Tết break (06–14/02, no swap) did not
add any trading closures. Both exchanges closed only Monday to Friday, 8–12 Feb.

## 2017 — 10 closed sessions; Tết Thu 26 Jan – Wed 1 Feb

| Date | Name | Sources |
|---|---|---|
| 2017-01-02 Mon | New Year's Day (observed) | H17, N17 |
| 2017-01-26, 01-27, 01-30, 01-31, 02-01 | Tet | H17, N17/HNX 237114 |
| 2017-04-06 Thu | Hung Kings Commemoration | H17, N17 |
| 2017-05-01 Mon | Reunification Day and Labour Day | H17, N17, HNX 251727 |
| 2017-05-02 Tue | Reunification Day and Labour Day (observed) | H17, N17, HNX 251727 |
| 2017-09-04 Mon | National Day (observed) | H17, HNX 262627, HNX 60001094 |

## 2018 — 11 closed sessions; Tết Wed 14 – Tue 20 Feb

| Date | Name | Sources |
|---|---|---|
| 2018-01-01 Mon | New Year's Day | H18, N18, HNX 269414 |
| 2018-02-14, 02-15, 02-16, 02-19, 02-20 | Tet | H18, N18, HNX 273021 |
| 2018-04-25 Wed | Hung Kings Commemoration | H18, N18, HNX 282312 |
| 2018-04-30 Mon, 05-01 Tue | Reunification Day and Labour Day | H18, N18, HNX 282312 |
| 2018-09-03 Mon | National Day (observed) | H18, N18, HNX 60002892 |
| 2018-12-31 Mon | New Year's Day; government swap with Sat 05/01/2019 | H19, N19, HNX 60003438 |

**HOSE-only unscheduled closures, not in the CSV.** On 22/01/2018 HOSE's
trading system failed in the closing ATC auction, and the exchange halted from
that auction onward. HOSE then did not trade at all on Tue **2018-01-23** or
Wed **2018-01-24**. It resumed on 25/01/2018. Sources:

- HOSE press releases: https://api.hsx.vn/n/api/v1/1/news/1077879 and https://api.hsx.vn/n/api/v1/1/news/1078085
- HOSE incident notice: https://api.hsx.vn/n/api/v1/1/news/1071819
- HSC: https://www.hsc.com.vn/en/25012018-ho-chi-minh-stock-exchange-resume-all-trading-activities

These are real non-sessions for HOSE only; nothing indicates that HNX or UPCoM
closed. They were left out because they are not holidays. If the calendar
should cover them, the rows would be:
`HOSE,2018-01-23,Trading system outage` and `HOSE,2018-01-24,Trading system outage`.

## 2019 — 11 closed sessions; Tết Mon 4 – Fri 8 Feb

| Date | Name | Sources |
|---|---|---|
| 2019-01-01 Tue | New Year's Day | H19, N19, HNX 60003438 |
| 2019-02-04 – 02-08 (Mon–Fri) | Tet | H19, N19, HNX 60003629 |
| 2019-04-15 Mon | Hung Kings Commemoration (observed); 10/3 lunar fell on Sun 14/4 | H19, N19, HNX 60004039 |
| 2019-04-29 Mon (swap with Sat 04/05), 04-30 Tue, 05-01 Wed | Reunification Day and Labour Day | H19, N19, HNX 60004039 |
| 2019-09-02 Mon | National Day | H19, N19, HNX 60005857 |

## 2020 — 10 closed sessions; Tết Thu 23 – Wed 29 Jan

| Date | Name | Sources |
|---|---|---|
| 2020-01-01 Wed | New Year's Day | H20, N20, HNX 60007028 |
| 2020-01-23, 01-24, 01-27, 01-28, 01-29 | Tet | H20, N20, HNX 60007288; press: https://baodautu.vn/lich-nghi-giao-dich-chung-khoan-dip-tet-nguyen-dan-canh-ty-2020-d114872.html |
| 2020-04-02 Thu | Hung Kings Commemoration | H20, N20, HNX 60007935 |
| 2020-04-30 Thu, 05-01 Fri | Reunification Day and Labour Day | H20, N20, HNX 60008208 |
| 2020-09-02 Wed | National Day | H20, N20, HNX 60009447 |

## 2021 — 11 closed sessions; Tết Wed 10 – Tue 16 Feb

| Date | Name | Sources |
|---|---|---|
| 2021-01-01 Fri | New Year's Day | H21, N21, HNX 60010797 |
| 2021-02-10, 02-11, 02-12, 02-15, 02-16 | Tet | H21, N21, HNX 60011068 |
| 2021-04-21 Wed | Hung Kings Commemoration | H21, N21, HNX 60011563 |
| 2021-04-30 Fri | Reunification Day and Labour Day | H21, N21 |
| 2021-05-03 Mon | Reunification Day and Labour Day (observed); 1/5 fell on Sat | H21, N21 |
| 2021-09-02 Thu, 09-03 Fri | National Day; 03/09 is the extra day under the 2019 Labour Code | H21, N21, HNX 60012732 |

## Venue differences

- **Holidays:** HOSE, HNX and UPCoM had identical holiday closures in every year.
- **HOSE outage, January 2018:** see the 2018 section.
- **Partial HOSE halts, not closures:** the 09/06/2020 ATC auction was
  cancelled (https://api.hsx.vn/n/api/v1/1/news/1193288), and trading stopped
  for the afternoon of 01/06/2021 (https://api.hsx.vn/n/api/v1/1/news/1623158).
  Both days still had a session.

## Cross-check against index histories (check only — no dates were changed)

I compared the recorded closures with daily index bars for every weekday from
2016-01-04 to 2021-12-31. The bar sources were:

- SSI iBoard: `https://iboard-api.ssi.com.vn/statistics/charts/history?resolution=1D&symbol=<VNINDEX|HNXINDEX|HNXUPCOMINDEX>`
- Entrade: `https://services.entrade.com.vn/chart-api/v2/ohlcs/index?symbol=VNINDEX&resolution=1D`

**Recorded closure that still traded:** none. No index has a bar on any of the
63 recorded closures, for any venue.

**Weekdays with no bar that I did not record:**

| Date(s) | Series | Finding |
|---|---|---|
| **2018-01-23, 2018-01-24** | VN-Index (Entrade has no bar; SSI has only a flat, zero-volume bar repeating the 22/01 close) | **A real HOSE non-session.** This is the trading-system outage described in the 2018 section. It is a disagreement with the CSV, by design. |
| 23 month-end dates, 2016–2018, plus 2017-05-05 and 2017-07-28 | SSI VN-Index only | Data gaps in SSI. Entrade VN-Index has bars on all of them. |
| 33 dates in 2020–2021 (e.g. 2021-01-21 to 02-04, 2021-07-12 to 07-14) | Entrade VN-Index only | Data gaps in Entrade. SSI VN-Index has bars on all of them. |
| 2017-05-29 | SSI HNX-Index and UPCoM-Index | Data gap. On that date SSI has normal volume bars for the HNX stocks PVS, SHB and ACB and the UPCoM stocks ACV, VIB and MCH. |
| 2020-08-31 | SSI HNX-Index | Data gap. Entrade's HNX series has a bar on that date. |

After these explanations, the only weekdays without a session that the CSV
does not record are HOSE's 2018-01-23 and 2018-01-24.
