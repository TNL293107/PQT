# ADR-041: Four real figures in a test fixture — what happened and the check that prevents it

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
public document would publish them again. They are identifiable from git
history: the test files at the parent of the fix commit `9befc39`, and the
git-ignored verification data they were first checked against,
`tmp/adr-038-verification/`.

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

## The data policy's steps ("If a licence is violated")

| Step | Status, 2026-11-16 |
| --- | --- |
| 1. Stop the ingestion path | Nothing to stop: no vendor data was imported and no ingestion path was running |
| 2. Delete the affected data from all storage | Nothing was imported (Exposure), so the database never held it. The git-ignored copy in `tmp/adr-038-verification/` is kept, outside git, as the scanner's reference. The figures are still in the history of the branches that contain `73588e3` (see Remedy) |
| 3. Verify nothing reached Git history | **Not met.** Verified, and the answer is that it did: `73588e3` is on three remote branches of a public repository. The Remedy leaves it there, by the Moderator's decision, and keeps it out of `develop`, `uat`, `staging` and `main` |
| 4. Record it in an ADR | This ADR |

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

"Not rewritten" holds only for the feature branches. **`73588e3` must never
become an ancestor of `develop`, `uat`, `staging` or `main`.** A merge commit
would carry it, and with it all four figures, into their history. So:

- PR #20 (`feature/phase-7-vietcap-statements-code`), the schema-6 writer
  branch (`feature/phase-7-manifest-v6-writer`), and **every other branch that
  contains `73588e3`**, remote or local, merge only by **squash**, or after a
  rebase that drops `73588e3`.
- Before and after each such merge, the merger checks
  `git merge-base --is-ancestor 73588e3 origin/develop`, which must fail (exit
  1). As of 2026-11-16 it fails for `develop`, `uat`, `staging` and `main`.

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
- The data policy's rule 3 now states which real figures a fixture may hold:
  an as-filed IDS or HOSE figure may appear in a test fixture or an evidence
  doc, and a figure only a vendor supplies never may (the Moderator,
  2026-11-17). This ADR adds the check that enforces the second half for
  fixtures built next to real data.
- The scanner lives in `tools/real-figure-scan/`, so fixture authors can run it
  (rule 3); the first one stays in the git-ignored `tmp/`. CI's "Repository checks" runs it
  over every test file and fixture against `denylist.txt`, which holds salted
  scrypt digests of real figures, so CI needs no real data and never prints a
  figure. Docs are not scanned in CI yet.
- **CI protects only against the vendor figures known to have been committed.**
  `denylist.txt` holds six digests: the four of this incident and the two VNM
  figures ADR-038 and the Phase 6 definition of done quoted (PR #22 H4,
  redacted in `d10960d`). Any other vendor figure passes CI. A rescaled figure
  (written in millions) and anything under 10^9 are not detected at all
  (README, "Limits").
- **The full scan runs locally.** The verification data,
  `tmp/adr-038-verification/`, was restored from a USB copy on 2026-11-17 (not
  re-fetched, as the Moderator decided before the legal review). A scan against
  it, or against the same 10,197 figures as digests in the git-ignored
  `tmp/real-figure-scan/denylist-full.txt`, checks every non-round figure of at
  least 10^9 in that data. It is not CI's denylist because Vietcap's values
  equal the public IDS forms to the đồng, and CI would then fail on IDS figures
  rule 3 allows.
- **The full scan's first run found 13 hits, none from a vendor.** All were
  committed in sprints 6.2, 6.3 and 8.1 (2026-10-13 to 10-22), before the first
  Vietcap statement was read on 10-30. QA + QC classified them on 2026-11-17,
  P = 10, V = 0, I = 3, and they stay; CI does not flag them. Lines as of
  `develop` at `3801a6b`:
  - **P, an as-filed IDS figure (10):**
    `backend/tests/PersonalQuant.IntegrationTests/FundamentalsPersistenceTests.cs:43`
    and `:45`; `backend/tests/PersonalQuant.UnitTests/Fundamentals/StatementLineTests.cs:68`;
    `backend/tests/PersonalQuant.UnitTests/Fundamentals/StatementLinesTests.cs:27`
    (two figures) and `:29`;
    `backend/tests/PersonalQuant.UnitTests/Fundamentals/StatementNormaliserTests.cs:169`
    and `:180` (two figures); `docs/roadmap/evidence/sprint-8-1-steps.md:10`.
  - **V, a figure only a vendor supplies (0).**
  - **I, an invented placeholder that matches a vendor value by chance (3):**
    `backend/tests/PersonalQuant.UnitTests/Fundamentals/FundamentalRatioServiceTests.cs:469`,
    `:912` and `:929` (`:121`, `:564` and `:581` at `9b9d688`, where QA + QC classified them).

  A full local scan therefore reports these 13. Any other hit is new and must
  be looked at.
