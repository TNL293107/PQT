# Venue history and entitlement sources

Research date 2026-10-13. Calendar reference: E:/PQT/data/fixtures/trading-calendar.csv.

## Part 1 - exchange transfers

### MCH (Masan Consumer) - UPCOM -> HOSE
- Last UPCOM session 2025-12-17; first HOSE session 2025-12-25 (HOSE first-day reference 212,800, the average of the 30 last UPCOM reference prices 6/11-17/12). Source: VnEconomy 12/12/2025, quoting HNX's decision to cancel UPCOM registration and HOSE's listing approval (received 11/12/2025): https://vneconomy.vn/mch-se-len-san-hose-vao-ngay-2512-toi.htm ; confirmation of first session: https://thoibaotaichinhvietnam.vn/hon-1-ty-co-phieu-cua-masan-consumer-chinh-thuc-giao-dich-tren-hose-189400.html
- Cross-check against CafeF raw bars: last UPCOM bar 17/12/2025 (212.5), next bar 25/12/2025 (223.5, "+10.70 (+5.03%)" i.e. vs 212.8 reference). No bars 18-24/12.
- Primary HOSE/HNX notice not fetched (hsx.vn/hnx.vn not read directly); press quotes them.

### BSR (Binh Son Refining) - UPCOM -> HOSE
- Last UPCOM session 2025-01-06; UPCOM registration cancelled from 2025-01-07; first HOSE session 2025-01-17, reference 21,300, +/-20% first-day band. Source: Doanh nhan / Bao Phap luat 09/01/2025 quoting HOSE's notice: https://doanhnhan.baophapluat.vn/loc-hoa-dau-binh-son-bsr-chot-ngay-giao-dich-chinh-thuc-tren-hose-80130.html ; also https://tapchicongthuong.vn/loc-hoa-dau-binh-son--bsr-chot-ngay--chuyen-nha--va-gia-niem-yet-tren-hose-131374.htm
- Note: some press (nhadautu.vn) misstates 17/1 as a UPCOM date; the HOSE notice as quoted says 17/01/2025 is the first HOSE session.

### LPB (LPBank) - no transfer in window
- LPB moved UPCOM -> HOSE in 2020: first HOSE session 2020-11-09 (ref 11,800), outside the 2023-2026 window. https://thoibaotaichinhvietnam.vn/co-phieu-lpb-chinh-thuc-chao-san-hose-31969.html . So 2023-08-22 was a HOSE session.

## Part 2 - entitlements behind the breaches

Exchange reference check: CafeF's history endpoint (cafef.vn/du-lieu/Ajax/PageNew/DataHistory/PriceHistory.ashx) reports each session's change against the exchange's own reference price ("ThayDoi"), so the reference the exchange set after the actions can be read off as close - change. Note that UPCOM's reference price is the previous session's volume-weighted average price, not its close, so on UPCOM a close-to-close move differs from the exchange's own move even with no action.

### LPB 2023-08-22, -20.88% - HOSE session; entitlements
- Venue: HOSE (on HOSE since 2020-11-09). -20.88% breaches HOSE +/-7% -> not venue mismatch.
- Two entitlements share record date 2023-08-23 (Wed), so ex-date 2023-08-22 (Tue, T+2 regime):
  - FY2022 stock dividend 19% (100:19), ~328.5m shares. LPBank announced 12/07/2023. https://www.sggp.org.vn/lpbank-chot-quyen-tra-co-tuc-bang-co-phieu-ty-le-19-post697236.html ; https://thoibaotaichinhvietnam.vn/co-dong-lpbank-sap-nhan-co-tuc-bang-co-phieu-ty-le-19-131935.html ; https://thitruongtaichinhtiente.vn/lpbank-thong-bao-ngay-chot-danh-sach-co-dong-chia-co-tuc-19-47882.html
  - Rights offer 500m shares at 10,000, ratio 100,000:28,916, "cũng vào ngày 23/8" record date; transfer 5/9-22/9/2023, subscription 5/9-26/9/2023. Tap chi Cong thuong 08/08/2023: https://tapchicongthuong.vn/dieu-gi-dang-thuc-day-co-phieu-lpb-cua-ngan-hang-lpbank-bat-tang-manh-108734.htm . The 28.916% is computed on pre-dividend shares (500m / 1,729.1m), consistent with the shared record date.
- VSD notice not located (vsd.vn search did not surface it). announced_on: 2023-07-12 for the dividend (LPBank's notice date per press); 2023-08-08 for the rights (earliest dated article found that states the rights record date - not the true first publication).
- Price check (CafeF raw): close 21/08/2023 19,400; 22/08 15,350, CafeF change "+0.25 (+1.66%)" -> exchange reference 15,100. Composed reference (19,400 + 0.28916*10,000)/(1 + 0.19 + 0.28916) = 15,070 -> +1.86%. Matches HOSE's 15,100 (tick-rounded).

### MCH 2025-02-11, -29.46% - UPCOM session; rights issue
- Venue: UPCOM (moved to HOSE 2025-12-25). -29.46% exceeds even UPCOM's +/-15% -> entitlement.
- VSDC notice "MCH: Right Issue; Postal Ballot", dated 03/02/2025: record date 12/02/2025, 1,000:451, 10,000 VND/share, 326,811,643 shares, transfer 18/02-04/03/2025, subscription to 10/03/2025. https://vsdc.vn/en/ad/178955 (press: https://tapchicongthuong.vn/masan-consumer--mch--chot-tang-von-them-45-trien-khai-ke-hoach-len-san-hose-128287.htm). The postal ballot on the same notice is for bondholders and is not a price entitlement.
- Ex-date 2025-02-11 (Tue), the session before the 12/02 record date.
- Price check: close 10/02/2025 219,300; 11/02 154,700, CafeF change "0 (0.00%)" -> UPCOM reference 154,700. (219,300 + 4,510)/1.451 = 154,246 from the close; (219,950 + 4,510)/1.451 = 154,700 if the 10/02 average price was ~219,950 (UPCOM references from the average). Restated close-to-close move +0.29%.

### MCH 2025-04-03 -12.14%, 2025-04-08 -14.14%, 2025-04-10 +13.16% - UPCOM sessions; venue mismatch, no action
- Venue: UPCOM. All three are inside UPCOM's +/-15% band (the April 2025 US-tariff selloff and rebound; 7/4/2025 was the Hung Kings holiday).
- No entitlement found going ex on these dates. MCH's 2025 actions found: rights ex 11/02/2025; AGM record 21/03/2025 (ex 20/03, no price effect); 1st 2025 advance cash dividend 2,500 ex 07/07/2025 (record 08/07); 2nd advance + bonus ex 09/01/2026.
- Price check: CafeF exchange-relative changes are -12.83% (03/04, ref 141.1), -7.18% (08/04, ref 118.4), +13.26% (10/04, ref 113.9). Each reference equals the previous session's traded value / volume (e.g. 04/04: 13.80bn / 116,559 = 118.4k), i.e. UPCOM's VWAP reference with no action adjustment.

### MCH 2026-01-09, -18.71% - HOSE session; entitlements
- Venue: HOSE (first HOSE session 2025-12-25). -18.71% breaches HOSE +/-7%.
- VSDC notice dated 26/12/2025, record date 12/01/2026 (Mon) -> ex 09/01/2026 (Fri): https://vsdc.vn/en/ad/190537
  - a. Distribution of treasury shares to existing shareholders 10,000:103 (1.03%).
  - b. Bonus issue from owner's equity 10,000:2,147 (21.47%).
  - c. 2nd advance 2025 cash dividend 2,500 VND/share, payment 30/01/2026.
  - d. Postal ballot (no price effect).
- Press: https://thuonghieucongluan.com.vn/tuan-5-9-1-mch-pnj-vci-dse-dong-loat-chot-quyen-co-tuc-thuong-co-phieu-cao-nhat-gan-46-5-a298483.html ; https://doanhnhan.baophapluat.vn/masan-consumer-mch-tang-von-len-sat-moc-13-000-ty-dong-tiep-tuc-chi-hon-2-600-ty-chia-co-tuc.html
- The treasury-share distribution is recorded as BonusShares 0.0103 (format has no treasury-distribution type; it is pro rata and lowers per-share value). Caller should decide whether to keep it: HOSE's own reference ignored it.
- Price check: close 08/01/2026 216,500; 09/01 176,000, CafeF change "-0.20 (-0.11%)" -> HOSE reference 176,200 = (216,500 - 2,500)/1.2147 (cash + 21.47% bonus only). With all three: (216,500 - 2,500)/(1 + 0.2147 + 0.0103) = 174,694 -> restated move +0.75%; without the treasury row 176,174 -> -0.10%. Both inside the band.

### BSR 2025-10-29, -33.85% - HOSE session; entitlements
- Venue: HOSE (first HOSE session 2025-01-17). Breaches HOSE +/-7%.
- VSDC notice "BSR: Payment of stock dividend; Share issuance due to raising share capital from owner's equity", dated 10/10/2025, record date 30/10/2025 (Thu) -> ex 29/10/2025 (Wed): https://vsdc.vn/en/ad/187952
  - Stock dividend 30% (100:30), from 2024 retained earnings (~930.1m shares).
  - Bonus issue from owner's equity (development investment fund) 31.5% (100:31.5) (~976.6m shares).
- Press stating ex-date 29/10: https://24hmoney.vn/news/12-doanh-nghiep-chot-quyen-co-tuc-ty-le-cao-nhat-61-5-c1a2667827.html ; https://vneconomy.vn/bsr-da-an-dinh-ngay-chia-co-phieu-thuong-ty-le-615.htm (10/10/2025).
- Price check: close 28/10/2025 26,000; 29/10 17,200, CafeF change "+1.10 (+6.83%)" -> HOSE reference 16,100. 26,000/(1 + 0.30 + 0.315) = 16,099 -> restated move +6.84% (a limit-up session, inside the band).

### TCX 2026-05-25, -14.90% - HOSE session; entitlement
- Venue: HOSE (listed directly on HOSE, see Part 1). Breaches HOSE +/-7%.
- VSDC notice "TCX: Payment of 2024 stock Dividend", dated 18/05/2026: record date 26/05/2026 (Tue), rate 5:1 (one new share per five held = 0.2). Ex-date 25/05/2026 (Mon). https://vsdc.vn/en/ad/195761
- One aggregator (24hmoney events page) lists GDKHQ 26/05/2026; that conflicts with the VSDC record date 26/05 and with the prices, and is taken as an error.
- Separate, not on this session: 2024 cash dividend 500 VND/share, record 08/04/2026 (ex 07/04/2026), paid 08/05/2026, VSDC notice 31/03/2026 https://vsdc.vn/en/ad/193993 . Not added to rows.csv (outside the breach list) - caller may want it: `TCX.HM,CashDividend,2026-04-07,,500,2026-04-08,2026-05-08,2026-03-31`.
- Price check: close 22/05/2026 51,000; 25/05 43,400, CafeF change "+0.90 (+2.12%)" -> HOSE reference 42,500 = 51,000/1.2. Restated move +2.12%. The 500 VND cash is not in this reference, consistent with it going ex in April.

(Part 1, continued - placed here because findings were written as sourced.)

### VPL (Vinpearl) - new listing, first HOSE session 2025-05-13
- transfers.csv row has empty from_venue / last_session_on_old: it is a listing, not a transfer.
- HOSE listing of 1,793,300,377 shares; first session 13/05/2025, reference 71,300 (close to the Feb-2025 public offering price of 71,350), +/-20% first-day band; closed at the 85,500 ceiling. HOSE announced the reference on 07/05/2025. https://baodautu.vn/vinpearl-tro-lai-san-hose-vao-135-dinh-gia-gan-5-ty-usd-d279174.html ; https://baodauthau.vn/niem-yet-va-chinh-thuc-giao-dich-co-phieu-vpl-cua-cong-ty-cp-vinpearl-post178689.html
- No UPCOM trading before: public-company registration Nov 2024, securities code Dec 2024, public offering early 2025, then straight to HOSE; first-day reference came from the offering price, not a UPCOM average. CafeF's VPL series starts 13/05/2025.
- Ticker reuse: an earlier Vinpearl was listed on HOSE as VPL from Jan 2008 and delisted Dec 2011 after merging into Vingroup (shares swapped into VIC). Outside the window, but the symbol VPL has two lives.

### TCX (Techcom Securities / TCBS) - new listing, first HOSE session 2025-10-21
- 2,311,308,021 shares; first session 21/10/2025, reference 46,800 = IPO price, +/-20% band; closed 48,950 (+4.59%). HOSE approval 09/10/2025. https://mekongasean.vn/hon-231-ty-co-phieu-tcbs-chinh-thuc-niem-yet-va-giao-dich-tu-ngay-2110-47675.html ; https://doanhnhan.baophapluat.vn/chinh-thuc-niem-yet-hose-von-hoa-tcbs-tcx-vuot-108000-ty-dong-ngay-phien-dau-tien-87885.html
- No UPCOM trading before: SSC recognised TCBS as a public company on 18/09/2025 after the IPO; listed directly on HOSE. Reference = IPO price. CafeF's TCX series starts 21/10/2025. (Vietstock's profile shows 20/10/2025 - appears wrong; every press source says 21/10.)

### DGC - no transfer in window
- HNX -> HOSE in 2020: last HNX session 17/07/2020, first HOSE session 28/07/2020 (ref 39,700). https://thitruongtaichinhtiente.vn/print/28418.html
- 2026: warning list (Apr), controlled list (moved out of VN30 13/05/2026), restricted trading from 26/05/2026 for late audited FY2025 statements - all HOSE status changes, not a venue change. Still trading on HOSE through 12/10/2026 per CafeF bars. No delisting found.

### SHB - no transfer in window
- HNX -> HOSE in 2021: last HNX session 05/10/2021, first HOSE session 11/10/2021 (ref 28,900). https://thoibaotaichinhvietnam.vn/shb-chinh-thuc-giao-dich-co-phieu-tren-hose-tu-ngay-1110-92542.html ; https://baophapluat.vn/shb-chinh-thuc-giao-dich-co-phieu-tren-hose-post416583.html

### Other VN30 members - no transfer found in window
- Systematic check: CafeF daily bars 2023-01-03..2026-10-12 for all 37 names, compared with the union of session dates (935 sessions). The only multi-session gaps are MCH 18-24/12/2025 (the transfer). No other name has a gap that a transfer would leave. A single missing 2025-08-14 on several T-V names (TPB VCB VHM VIB VIC VJC VNM VPB VRE, VPL) is a fetch artefact, not a market event.
- From prior knowledge (not re-sourced here; all before 2023): VIB, BCM, LPB moved UPCOM -> HOSE 2020; GVR, SSB listed 2021; SHB, DGC HNX -> HOSE 2021/2020.
- CafeF data notes for the caller: (1) BSR's CafeF series starts 20/01/2025 - it has no UPCOM history and no 17/01/2025 first HOSE bar (press: opened 22,500, closed 21,600 on 17/01). (2) MCH has single/multi-day holes on UPCOM: 2023-08-01, 2023-08-02, 2024-12-02..06, 2025-03-17 - not investigated (possibly suspension or no-trade days); not venue changes.

## Summary of the eight breaches

| Session | Ticker | Venue that day | Band | Raw move | Verdict |
| ------- | ------ | -------------- | ---- | -------- | ------- |
| 2023-08-22 | LPB | HOSE | 7% | -20.88% | Entitlement: 19% stock dividend + 100,000:28,916 rights at 10,000 (record 23/08) |
| 2025-02-11 | MCH | UPCOM | 15% | -29.46% | Entitlement: 1,000:451 rights at 10,000 (record 12/02) |
| 2025-04-03 | MCH | UPCOM | 15% | -12.14% | Venue mismatch, genuine move |
| 2025-04-08 | MCH | UPCOM | 15% | -14.14% | Venue mismatch, genuine move |
| 2025-04-10 | MCH | UPCOM | 15% | +13.16% | Venue mismatch, genuine move |
| 2025-10-29 | BSR | HOSE | 7% | -33.85% | Entitlement: 30% stock dividend + 31.5% bonus (record 30/10) |
| 2026-01-09 | MCH | HOSE | 7% | -18.71% | Entitlement: 2,500 cash + 21.47% bonus + 1.03% treasury distribution (record 12/01) |
| 2026-05-25 | TCX | HOSE | 7% | -14.90% | Entitlement: 5:1 (20%) stock dividend (record 26/05) |

Open points: LPB's VSD notice itself was not located (terms from press quoting LPBank); LPB rights announced_on is the earliest article found, not the true disclosure date; MCH treasury distribution row is a judgement call (HOSE's reference ignored it).

**Correction (MCH 2026-01-09 rows):** the schema keys an action on instrument + type + ex-date and rejects a repeated key within one file, so the bonus (0.2147) and the treasury distribution (0.0103) cannot be two BonusShares rows. rows.csv carries one `BonusShares` row at 0.225 = 0.2147 + 0.0103, which is also how the press reports the total (1,000:225, baophapluat 28/01/2026). Both pieces are pro rata to the same register, so they add. To match HOSE's reference instead, use 0.2147 (restated move -0.10% vs +0.75% with 0.225).
