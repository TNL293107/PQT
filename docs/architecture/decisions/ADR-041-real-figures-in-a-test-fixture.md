# ADR-041: Two real figures in a test fixture — what happened and the check that prevents it

**Status:** Accepted (Lead, 2026-11-16; the Moderator decided the remedy) · **Date:** 2026-11-16 · **Phase:** Sprint 11, item 11.4 (Phase 7)

The record that `data-policy.md` ("If a licence is violated", step 4) asks for.

## What happened

QA + QC's acceptance fixtures for the Vietcap statement provider (commit
`73588e3`, ADR-038) were meant to keep the real response shape with invented
numbers, as `data-policy.md` rules 1 and 3 require and as the Vietcap and CafeF
provider tests already do. Two of the numbers were not invented. They were one
listed company's real balance-sheet totals for one quarter (total assets and
equity), exactly as both IDS and Vietcap report them. The figures themselves are
deliberately not repeated here: quoting them in a public document would publish
them again. They are identifiable from the git-ignored verification data in
`tmp/adr-038-verification/`. They sat in
`VendorJoinTests.cs` and, as exponent doubles, in
`VietcapStatementProviderTests.cs`.

Tester + Audit found them while gating PR #20, by comparing the fixtures against
the verification data. The repository is public.

## Exposure

- Three remote branches carried the commit: `feature/phase-7-vietcap-statements`,
  `feature/phase-7-vietcap-statements-code` (PR #20) and
  `feature/phase-7-manifest-v6-writer`.
- `develop`, `uat`, `staging` and `main` never carried it.
- A scan of every number in `backend/tests` and `quant/tests` on those branches,
  against all 150 large figures in the verification data, found exactly these
  two and no other.
- No vendor data was imported; no ingestion path was running.

## Why it happened

The real responses had been read and saved for ADR-038's verification. The
fixtures were written next to them, by hand, and two values were taken from the
real data instead of being made up. Nothing checked the fixtures against the
real data: the rule existed only as a sentence in the data policy and as each
author's intention.

## Remedy (the Moderator's decision)

Because only two figures were exposed, the history is **not** rewritten. Both
figures are replaced with invented values in a new commit, every assertion
included, and that commit is carried onto each affected branch before any of
them merges. A scan after the fix must find no real figure.

## The check that prevents it

1. **Fixtures are invented, never copied.** A fixture value is written to be
   obviously synthetic (a round or patterned number, e.g. `74000000000000`), not
   transcribed from a response, however convenient.
2. **A gate scans for real figures.** When a PR adds or changes a fixture for a
   source whose real responses are held in `tmp/` (for example,
   `tmp/adr-038-verification/`), Tester + Audit scans every number in the
   changed test files against every large figure in that real data, including
   exponent forms (a figure written as a double, e.g. `1.234E13`). A single hit
   fails the gate.
3. **Fixture authors run the same scan before pushing.**
4. **Real responses stay in `tmp/`**, which is ignored by git, and are never
   copied into `backend/tests` or `quant/tests`.

## Consequences

- PR #20 and the schema-6 writer branch merge only after the replacement commit
  and a clean scan.
- The data policy's rule 3 is unchanged; this ADR adds the check that enforces
  it for fixtures built next to real data.
