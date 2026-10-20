# Sprint 7 plan — DRAFT v2, not approved

Status: Sprint 6 closed by the operator on 2026-10-20. This plan needs a
"yes" from Lead, Dev and the Moderator before any Sprint 7 code is written or
merged. It replaces draft v1 (which assumed Sprint 7 had not started).

## Where Sprint 7 stands

| # | Item | State |
| --- | --- | --- |
| 7.1 | Point-in-time ratios (ADR-030) | Code + tests pushed as a **proposal**; the unit suite is green (1,203, whole suite) (`badde9a`); ADR-030 Proposed |
| 7.2 | Surfaces: console `FA`, `GET /instruments/{id}/ratios` and `/fundamentals`, coverage row measured | Pushed as a **proposal** (`7e1bfa7`) |
| 7.3 | Dataset contract: fundamentals as of each session, manifest v4 + ADR | Not started |
| 7.4 | Phase 6 definition of done, evidenced | Not started |

## Proposed work

**A. Accept 7.1/7.2 (gate, no new code).** Lead reviews the pushed code against
ADR-030 and the sprint-plan wording; Dev answers findings. Accepted when:
the ADR status moves to Accepted, and the checks below pass.

**B0. Importer hotfix (Dev; approved by the Moderator 2026-10-20), before B.**
IDS shows the same form for a separate and a consolidated report filed the
same day (619 groups in 1,602 files; verified on FPT 2025-07-22), so stored
`Separate` reports carry consolidated numbers. Also unit errors: VPL stored in
millions, VCB and VIB EPS multiplied by 10^6. Work:
1. When a separate filing's tables equal the same period's consolidated tables,
   store values for the consolidated one only; record the separate as "filed,
   form identical to consolidated", with no values. A permanent importer rule
   with tests.
2. Fix units by a check (charter capital ÷ par must give a plausible share
   count; EPS against profit ÷ shares), not by ticker, then look for other names.
3. Investigate the 101 groups identical across different filing dates
   (re-filed report, or a mis-assigned tab) before deleting anything.
4. Re-import; regression: FPT on 2023-08-01 (P/E 18.5, P/B 4.4) unchanged or
   explained. Update ADR-029 (the "filing says the scope" assumption is wrong for
   the numbers). ADR-031's `scope` column is fixed after this.

**B. Close ADR-030's open checks (Dev, 7.1).** Both were promised in the ADR
and are not yet code:
1. Charter-capital check: compare capital across adjacent quarters with each
   action's share factor; apply a missing action and record a finding; refuse
   an action that would count twice.
2. Par check: profit ÷ shares against reported EPS; record a finding on
   disagreement.

**C. 7.3 Dataset contract (Dev code, Lead ADR-031).** Fundamentals carried per
session in the export (manifest v4 with a published schema), so research reads
them through the canonical dataset. Rules: ratios and underlying lines follow
the same as-of cut as 7.1; the manifest records which reports and which
corporate actions each value used; v1–v3 manifests still verify as before.
Lead writes ADR-031 first and Dev starts code only after Lead and Dev agree it.

**D. 7.4 Definition of done (Lead).** Written against evidence: real names,
as-of reads proving no later publication or restatement leaks, coverage
showing STB and GVR as *not collected* (coverage is per ticker, so HPG, which
holds 52 filings, shows Held; its two missing consolidated filings are recorded
in 7.4 and ADR-029, and no per-period coverage is added), and the backlog
below listed rather than hidden.

## Backlog found, not in Sprint 7 scope unless the Moderator adds it

- ACB lifted capital 15% in 2023 Q2 with no matching corporate action.
- MBB has two charter-capital values for 2022 Q4 and 2023 Q4.
- ACB 2022 Q1–Q3 forms on IDS carry no values (no trailing EPS then).
- STB, GVR, HPG ×2 (consolidated Q2 and half-year 2022): IDS capture gap.
- 22 filings refused by the importer (LPB 11, 11 with implausible filing dates).
- VPL has filings only from 2025 (listed 2025-05-13): ratios start later by nature.

## Ownership and boundaries

- Dev: `backend/` code for B and C. Lead: review of A, ADR-031, 7.4, docs.
- The other side's directory is touched only after a message.
- Commits end with no Co-Authored-By; no push without the Moderator's say-so.

## Verification

- Hand-computed case per ratio and one synthetic restatement per ratio.
- As-of test: a value at day D uses nothing published after D, for every
  field carried in the export.
- Manifest v1–v3 verification unchanged; a v4 manifest round-trips.
- `dotnet test` unit suite green; integration tests for the new endpoints and
  the export run in Docker.

## Decisions for the Moderator

Answered (relayed by Dev, 2026-10-20): (1) accept 7.1/7.2 after Lead's review,
no revert; (3) B before C, ADR-031 in parallel. **Still open: (2) whether the
backlog is out of Sprint 7.** Until it is answered the backlog stays out.

1. Accept 7.1/7.2 after review (A), or revert and redo under this plan?
2. Is the backlog above out of scope for Sprint 7?
3. Order (agreed by Lead and Dev, for the Moderator to confirm): **B before C
   code.** C exports ratios per session, so they must already pass the
   charter-capital check or manifest v4 would change. Lead writes ADR-031 in
   parallel while Dev does B.

## Agreement

- [x] Lead
- [x] Dev (with the three edits above, applied)
- [x] Moderator on points 1 and 3 (relayed by Dev); point 2 open
