# ADR-041: Three real figures in a test fixture — what happened and the check that prevents it

**Status:** Accepted (Lead, 2026-11-16; the Moderator decided the remedy) · **Date:** 2026-11-16 · **Phase:** Sprint 11, item 11.4 (Phase 7)

The record that `data-policy.md` ("If a licence is violated", step 4) asks for.

## What happened

QA + QC's acceptance fixtures for the Vietcap statement provider (commit
`73588e3`, ADR-038) were meant to keep the real response shape with invented
numbers, as `data-policy.md` rules 1 and 3 require and as the Vietcap and CafeF
provider tests already do. Three of the numbers were not invented:

- one listed company's real balance-sheet totals for one quarter (total assets
  and equity), exactly as both IDS and Vietcap report them, in
  `VendorJoinTests.cs` and, as exponent doubles, in
  `VietcapStatementProviderTests.cs`;
- one real net-sales value, copied verbatim in exponent form into a doc comment
  of `VietcapStatementProviderTests.cs`.

The balance-sheet totals were also in `VendorStatementLinesTests.cs`, a third
file. A fourth figure sat there too: one real equity-attributable-to-parent
value in an assertion. The fix commit (`9befc39`) replaced it along with the
other three, and the Moderator ruled it real on 2026-11-16.

The figures themselves are deliberately not repeated here: quoting them in a
public document would publish them again. They are identifiable from the
git-ignored verification data in `tmp/adr-038-verification/`.

Tester + Audit found the first two while gating PR #20, by comparing the
fixtures against the verification data, and the third with a scanner written for
this check. The repository is public.

## Exposure

- Three remote branches carried the commit: `feature/phase-7-vietcap-statements`,
  `feature/phase-7-vietcap-statements-code` (PR #20) and
  `feature/phase-7-manifest-v6-writer`.
- `develop`, `uat`, `staging` and `main` never carried it.
- **Lead's first scan was wrong.** It reported exactly two figures and missed the
  third, because that one sat in a comment and in exponent form. Tester's scanner
  reads every added line, comments included, and every number form (plain, `.0`,
  exponent, with separators) against the roughly 10,000 large figures in the
  verification data; it found all three, on all three branches, and none
  elsewhere. That miss is itself the case for one shared, mechanical check rather
  than an ad hoc one.
- No vendor data was imported; no ingestion path was running.

## Why it happened

The real responses had been read and saved for ADR-038's verification. The
fixtures were written next to them, by hand, and three values were taken from
the real data instead of being made up. Nothing checked the fixtures against the
real data: the rule existed only as a sentence in the data policy and as each
author's intention.

## Remedy (the Moderator's decision)

Because so few figures were exposed, the history is **not** rewritten. All four
are replaced with invented values in a new commit, every assertion and comment
included, and that commit is carried onto each affected branch before any of
them merges. The scanner must print PASS after the fix.

## The check that prevents it

1. **Fixtures are invented, never copied.** A fixture value is written to be
   obviously synthetic (a round or patterned number, e.g. `74000000000000`), not
   transcribed from a response, however convenient. That includes examples in
   comments.
2. **A gate scans for real figures.** When a PR adds or changes a fixture for a
   source whose real responses are held in `tmp/` (for example,
   `tmp/adr-038-verification/`), Tester + Audit runs
   `python tools/real-figure-scan/scan.py --reference <real data> --diff <base> <head>`
   ([README](../../../tools/real-figure-scan/README.md)). It checks every
   number of at least 10^9 on every added line of the changed test files,
   **comments included**, in **every form** (plain, `.0`, exponent such as
   `1.234E13`, with separators), against every large figure in that real data.
   A single hit fails the gate. A round number (one non-zero digit, such as
   `1.0E11`) is exempt: it collides by chance, not by copying.
3. **Fixture authors run the same scanner before pushing**; it must print PASS.
4. **Real responses stay in `tmp/`**, which is ignored by git, and are never
   copied into `backend/tests` or `quant/tests`.

## Consequences

- PR #20 and the schema-6 writer branch merge only after the replacement commit
  and a clean scan.
- The data policy's rule 3 is unchanged; this ADR adds the check that enforces
  it for fixtures built next to real data.
- The scanner lives in `tools/real-figure-scan/`, so fixture authors can run it
  (rule 3). The first one, in `tmp/`, was lost. CI's "Repository checks" runs it
  over every test file and fixture against `denylist.txt`, which holds salted
  scrypt digests of real figures, so CI needs no real data and never prints a
  figure. Docs are not scanned in CI yet.
- **CI protects only the four figures of this incident.** As of 2026-11-16,
  `denylist.txt` holds those four and nothing else. The verification data was
  lost, and the Moderator decided not to re-fetch it before the legal review, so
  any other real figure passes CI. A rescaled figure (written in millions) and
  anything under 10^9 are not detected at all (README, "Limits").
