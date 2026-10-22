# HOSE price-limit breaches: sourced entitlements

Prices (thousand VND) are CafeF raw closes from the PriceHistory endpoint
(`cafef.vn/du-lieu/Ajax/PageNew/DataHistory/PriceHistory.ashx`), fetched 2026-10-13.
CafeF's `ThayDoi` field is the change vs the exchange's reference price for that
session; it is noted only as a cross-check, never used to derive terms.

## ACB 2024-05-31 (raw -16.16%, prev close 29,400 -> 24,650)

- Actions: FY2023 cash dividend 1,000 VND/share (10%) + stock dividend 100:15. Record date 03/06/2024, so ex-date 31/05/2024 (Fri).
- VSD notice "ACB: Trả cổ tức năm 2023 bằng tiền và cổ phiếu", dated 23/05/2024: https://vsdc.vn/vi/ad/171314
- Payment date: VSD notice prints "13/06/2023" (evident typo, the notice is 2024); press gives 13/6 (2024): https://doanhnhan.baophapluat.vn/ngan-hang-acb-sap-chi-hon-3880-ty-dong-tien-tra-co-tuc-bang-tien-mat-76813.html . Recorded as 2024-06-13 — flag: secondary-sourced year.
- Check: ref = (29,400 - 1,000)/1.15 = 24,696; 24,650 vs 24,696 = -0.18% (CafeF reference change -0.20%). Explained.

## How the rest were found

VSD's entitlement calendar ("Lịch thực hiện quyền", vsdc.vn/vi/lich-giao-dich?tab=LICH_THQ)
was searched by ticker over a window of roughly ±10 days around each candidate record date
(the session after the breach). Each breach returned exactly one HOSE-equity entitlement notice,
whose record date is the next session, so the breach session is the ex-date (T+2: ex-date =
one session before record date). `announced_on` is the VSD notice's "Cập nhật ngày" date (the
fixture's convention: latest evidenced date, not the issuer's first disclosure). Notice links
are given on vsdc.vn (www.vsd.vn redirects there; its TLS cert only covers vsdc.vn).
"StockDividend" = paid from retained profit ("trả cổ tức bằng cổ phiếu"); "BonusShares" =
issue from owners' equity ("tăng vốn cổ phần từ nguồn vốn chủ sở hữu"), matching the
fixture's existing FPT 2025 / GAS 2024 / SSI rows.

## BID 2023-11-28 (raw -9.65%, 43,000 -> 38,850)

- Action: stock dividend 100:12,69 (from 2021 retained profit). Record date 29/11/2023.
- VSD notice "BID: Phát hành cổ phiếu để chi trả cổ tức từ nguồn lợi nhuận còn lại sau thuế, sau trích lập các quỹ năm 2021", updated 17/11/2023: https://vsdc.vn/vi/ad/164617
- Press: https://mekongasean.vn/bidv-chot-ngay-tra-co-tuc-bang-co-phieu-ty-le-1269-15682.html
- Check: 43,000/1.1269 = 38,158; 38,850 -> +1.81%. Explained. (CafeF's ThayDoi field for this session is NOT adjusted, -9.65%; its adjusted series is.)
- No cash dividend in the same notice / window.

## CTG 2023-11-30 (raw -10.54%, 29,400 -> 26,300)

- Action: stock dividend 100:11,7415 ("1.000.000 cổ phiếu được nhận 117.415 cổ phiếu mới"), from 2020 retained profit. Record date 01/12/2023.
- VSD notice "CTG: Phát hành cổ phiếu để trả cổ tức (từ lợi nhuận sau thuế, sau trích quỹ và chia cổ tức bằng tiền mặt năm 2020)", updated 22/11/2023: https://vsdc.vn/vi/ad/164888
- Check: 29,400/1.117415 = 26,311; 26,300 -> -0.04%. Explained. (CafeF ThayDoi again unadjusted.)

## FPT 2023-07-05 (raw -14.02%, 87,000 -> 74,800)

- Actions: remaining FY2022 cash dividend 1,000 VND/share (paid 13/07/2023) + FY2022 stock dividend 20:3 (0.15). Record date 06/07/2023.
- VSD notice "FPT: Trả cổ tức còn lại năm 2022 bằng tiền và phát hành cổ phiếu để trả cổ tức năm 2022", updated 23/06/2023: https://vsdc.vn/vi/ad/159649
- Check: (87,000 - 1,000)/1.15 = 74,783; 74,800 -> +0.02% (CafeF ThayDoi 0.00%). Explained.

## FPT 2024-06-12 (raw -9.90%, 146,500 -> 132,000)

- Actions: remaining FY2023 cash dividend 1,000 VND/share (paid 20/06/2024) + 20:3 bonus issue from owners' equity (0.15). Record date 13/06/2024.
- VSD notice "FPT: Trả cổ tức còn lại năm 2023 bằng tiền; Phát hành cổ phiếu để tăng vốn cổ phần từ nguồn vốn chủ sở hữu cho cổ đông hiện hữu", updated 05/06/2024: https://vsdc.vn/vi/ad/171655
- Check: (146,500 - 1,000)/1.15 = 126,522; 132,000 -> +4.33% (CafeF ThayDoi +4.33%). Explained.
- Type note: the share leg is a capital increase from equity, so BonusShares (the 2023 one was a dividend from profit, so StockDividend).

## GAS 2023-09-22 (raw -14.65%, 106,500 -> 90,900)

- Action: bonus shares 10:2 (0.2) from owners' equity. Record date 25/09/2023.
- VSD notice "GAS: Nhận cổ phiếu phát hành do thực hiện tăng vốn cổ phần từ nguồn vốn chủ sở hữu", updated 14/09/2023: https://vsdc.vn/vi/ad/161638
- Check: 106,500/1.2 = 88,750; 90,900 -> +2.42% (CafeF ThayDoi +2.42%). Explained.
- The VSD search for GAS 10/09-05/10/2023 returned only this notice; no cash dividend went ex this session (CafeF ThayDoi agrees: reference 88,750 = 106,500/1.2).

## HDB 2023-07-19 (raw -10.85%, 18,900 -> 16,850)

- Action: FY2022 stock dividend 15% (100 -> 15 new). Record date 20/07/2023.
- VSD notice "HDB: Trả cổ tức năm 2022 bằng cổ phiếu", updated 04/07/2023: https://vsdc.vn/vi/ad/159807
- Check: 18,900/1.15 = 16,435; 16,850 -> +2.53%. Explained.

## MBB 2023-07-14 (raw -11.61%, 21,100 -> 18,650)

- Action: stock dividend 15% (100 -> 15) from accumulated 2022 retained profit. Record date 17/07/2023.
- VSD notice "MBB: Trả cổ tức bằng cổ phiếu từ lợi nhuận sau thuế để lại lũy kế của năm 2022", updated 07/07/2023: https://vsdc.vn/vi/ad/159900
- Check: 21,100/1.15 = 18,348; 18,650 -> +1.65%. Explained.

## SAB 2023-09-14 (raw -48.92%, 166,800 -> 85,200)

- Action: 1:1 bonus issue from owners' equity. Record date 15/09/2023.
- VSD notice "SAB: Phát hành cổ phiếu để tăng vốn cổ phần từ nguồn vốn chủ sở hữu", updated 29/08/2023: https://vsdc.vn/vi/ad/161287
- Check: 166,800/2 = 83,400; 85,200 -> +2.16% (CafeF ThayDoi +2.16%). Explained.

## SHB 2023-07-24 (raw -12.50%, 14,400 -> 12,600)

- Action: FY2022 stock dividend 100:18. Record date 25/07/2023.
- VSD notice "SHB: Trả cổ tức năm 2022 bằng cổ phiếu", updated 06/07/2023: https://vsdc.vn/vi/ad/159838
- Check: 14,400/1.18 = 12,203; 12,600 -> +3.25%. Explained.

## TCB 2024-06-20 (raw -48.65%, 48,300 -> 24,800)

- Action: 1:1 bonus issue from owners' equity. Record date 21/06/2024.
- VSD notice "TCB: Thực hiện phát hành cổ phiếu để tăng vốn cổ phần từ nguồn vốn chủ sở hữu", updated 12/06/2024: https://vsdc.vn/vi/ad/171880
- Check: 48,300/2 = 24,150; 24,800 -> +2.69% (CafeF ThayDoi +2.69%). Explained.
- The VSD search for TCB 10-30/06/2024 returned only this notice; no cash dividend went ex this session.

## VCB 2023-07-25 (raw -12.33%, 104,600 -> 91,700)

- Action: stock dividend 1.000:181 (0.181) from remaining 2019-2020 profit. Record date 26/07/2023.
- VSD notice "VCB: Phát hành cổ phiếu để trả cổ tức từ nguồn lợi nhuận còn lại năm 2019, 2020 ...", updated 12/07/2023: https://vsdc.vn/vi/ad/159971
- Check: 104,600/1.181 = 88,569; 91,700 -> +3.54% (CafeF ThayDoi +3.50%, tick-rounded reference). Explained.

## VPB 2024-05-22 (raw -7.58%, 19,800 -> 18,300)

- Action: FY2023 cash dividend 1,000 VND/share, paid 31/05/2024. Record date 23/05/2024.
- VSD notice "VPB: Trả cổ tức năm 2023 bằng tiền", updated 14/05/2024: https://vsdc.vn/vi/ad/170958
- Check: 19,800 - 1,000 = 18,800; 18,300 -> -2.66% (CafeF ThayDoi -2.66%). Explained.

## LPB 2024-12-27 (raw -8.55%, 33,900 -> 31,000)

- Action: FY2023 stock dividend 16,8% (1,000 -> 168 new). Record date 30/12/2024 (Mon), ex-date 27/12/2024 (Fri).
- VSD notice "LPB: Trả cổ tức năm 2023 bằng cổ phiếu", updated 19/12/2024: https://vsdc.vn/vi/ad/177786
- Check: 33,900/1.168 = 29,024; 31,000 -> +6.81% (CafeF ThayDoi +6.79%). Explained, but close to the band: inside ±7%, a near-limit-up session from the reference.

## DGC 2024-03-07 (raw +8.92%, 111,000 -> 120,900) — NO corporate action

- VSD entitlement calendar search for DGC, 25/02/2024-20/03/2024: no notices at all. An entitlement could only lower the reference, never explain an up-move.
- CafeF's own ThayDoi for 07/03/2024 is "+7,90 (+6,99%)", i.e. a reference of 113,000 and a limit-up close (113,000 x 1.07 = 120,910 -> 120,900). But CafeF's 06/03/2024 close is 111,000 (its ThayDoi "-2,40" from 113,400). The two CafeF fields disagree by 2,000.
- Cross-check, VNDirect finfo API (https://api-finfo.vndirect.com.vn/v4/stock_prices?sort=date&q=code:DGC~date:gte:2024-03-04~date:lte:2024-03-08): 06/03/2024 close 113.0 (low 109.8, average 111.42); 07/03/2024 basicPrice (reference) 113.0, ceiling 120.9, close 120.9 = +6.99%. So CafeF's 06/03/2024 close of 111,000 is a bad bar (true close 113,000; 06/03 is then -0.35%, 07/03 a limit-up +6.99%). Not a corporate action: fix the bar, not the adjustments. No row written.


## Capital steps with no action (sprint 8.1, QA's `docs/roadmap/evidence/sprint-8-1-steps.md`)

Not price-band breaches: these two rows were missing, so the ratio service
reported each charter-capital step as unexplained (ADR-032). Both notices were
read on vsdc.vn on 2026-10-21. `announced_on` is the notice's "Cập nhật ngày"
date, as above.

### ACB 2023-06-01 (FY2022 dividend)

- Actions: FY2022 cash dividend 10% (1,000 VND/share) plus stock dividend 100:15. Record date 02/06/2023 (Fri), ex-date 01/06/2023 (Thu). Cash paid 12/06/2023.
- VSD notice "ACB: Trả cổ tức năm 2022 bằng tiền và cổ phiếu", updated 23/05/2023: https://vsdc.vn/vi/ad/159023
- Check: capital 2023Q1 → 2023Q2 is +15.0% (₫33,774bn → ₫38,841bn), the stock dividend's factor.

### HPG 2024-05-23 (bonus issue)

- Action: shares issued from owners' equity ("tăng vốn cổ phần từ nguồn vốn chủ sở hữu"), 10:1, so `BonusShares` 0.1. Record date 24/05/2024 (Fri), ex-date 23/05/2024 (Thu).
- VSD notice "HPG: Nhận cổ phiếu phát hành do thực hiện tăng vốn cổ phần từ nguồn vốn chủ sở hữu", updated 10/05/2024: https://vsdc.vn/vi/ad/170903
- Check: capital 2024Q1 → 2024Q2 is +10.0%, the bonus factor.
