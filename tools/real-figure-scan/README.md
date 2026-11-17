# Real-figure scanner

The check [ADR-041](../../docs/architecture/decisions/ADR-041-real-figures-in-a-test-fixture.md)
asks for: a test fixture must be invented, never copied from a real vendor
response (`docs/data-policy.md`, rules 1 and 3).

It reads every number of at least 10^9 in the files in scope, **comments
included**, in **every form** (plain, `.0`, exponent such as `1.234E13`,
`_` / `,` / `.` / space / no-break space / apostrophe thousands separators,
a fraction after any of them, C# suffixes such as `m`, the JS BigInt `n`, a
glued `VND`, `đ` or `₫`, and a number glued inside an identifier or after a dot,
fraction and exponent included, such as `value1.2345E13`), and compares it
with a set of real figures. One match fails. A round number (one significant
digit, such as `1.0E11`) is exempt, because it collides by chance.

## Limits: what it does not detect

- **A rescaled figure.** `12,345,678.9 million`, `12.3 bn`, or a figure written
  in thousands or millions is below 10^9 as written, or no longer equal to the
  real value. No pattern can undo a rescale, so it is not attempted.
- **Anything under 10^9**, by design.
- **A real round figure** (one significant digit), by design: it cannot be told
  from an invented one.
- **A figure glued inside a run of 32 or more digits.** Such a run (a hash, an
  id) is above 10^30 and is skipped whole, so nothing inside it is read. Copying
  a figure into a hash is not a copying pattern, so this is accepted.
- **A figure outside the reference set** (see below).

Standard library only, Python 3.12 or later. Nothing to install.

## The real figures never enter git

| Source | What it is | Where it runs |
| --- | --- | --- |
| `--reference DIR` | Real responses in a git-ignored directory, e.g. `tmp/adr-038-verification/`. Every large number in every file | On the machine that holds the data |
| `--denylist FILE` | Salted scrypt digests of real figures, made with `--make-denylist`. A digest does not reveal its figure | Anywhere, including CI |

There are two modes, and they protect against different sets.

| Mode | Figures | Command |
| --- | --- | --- |
| **CI** | The six in `denylist.txt` (below) | `--denylist tools/real-figure-scan/denylist.txt` |
| **Local, full** | All 10,196 non-round figures of at least 10^9 in the ADR-038 verification data, `tmp/adr-038-verification/` (restored from a USB copy on 2026-11-17, not re-fetched), plus the sixth known figure | `--reference tmp/adr-038-verification`, or the same set as digests in the git-ignored `tmp/real-figure-scan/denylist-full.txt` (10,197) |

CI does not use the full set because Vietcap's values equal the public IDS
forms to the đồng, so it would fail on IDS figures the data policy allows (rule
3). **The full local scan reports 13 known hits**, all from sprints 6.2, 6.3 and
8.1, before any vendor statement was read: 10 as-filed IDS figures and 3
invented placeholders that match a vendor value by chance (ADR-041,
Consequences; QA + QC's triage in the git-ignored
`data/tmp/legacy-hits-triage.md`). A 14th hit is new and must be looked at.

`denylist.txt` holds the digests of the **six** real vendor figures known to
have been committed:

- the **four** of the ADR-041 incident: the three the ADR names, and the
  equity-to-parent figure asserted in `VendorStatementLinesTests.cs`, which the
  same fix commit (`9befc39`) replaced and the Moderator ruled real on
  2026-11-16;
- the **two** VNM 2024Q4 figures (revenue and profit) that ADR-038 and the
  Phase 6 definition of done quoted on `develop`, removed by `d10960d`.

Five of the six are in the verification data; the sixth comes from the numbers
`9befc39` removed. A green "Repository checks" means none of the six came back,
not that a fixture is invented: any other vendor figure still passes CI, and
only the full local scan catches it.

**Brute force.** A digest is salted and slow, but the space it hides is small:
a 13- to 14-digit figure whose rough size is known can be found by trying
candidates. This is accepted because the figures are a listed company's own
published statements; what ADR-041 prevents is the repository redistributing
the vendor's data, and a digest does not do that.

When real data is saved for a new verification, rebuild the local full
denylist from it. Only a vendor figure found committed goes into the CI
`denylist.txt`:

```bash
python tools/real-figure-scan/scan.py --reference tmp/adr-038-verification --reference tmp/<verification> \
    --make-denylist tmp/real-figure-scan/denylist-full.txt
# A vendor figure found committed: put it alone in a git-ignored directory, then
python tools/real-figure-scan/scan.py --reference tmp/<that directory> --make-denylist tmp/new.txt
# and append the digest line of tmp/new.txt to tools/real-figure-scan/denylist.txt
```

A hit is printed as `path:line` and the figure's digit count, never the figure,
because the CI log of a public repository is public.

## Run

```bash
# Before pushing a fixture (ADR-041 rule 3): the lines your branch adds.
python tools/real-figure-scan/scan.py --reference tmp/adr-038-verification --diff origin/develop HEAD

# Full local scan: expect exactly the 13 known hits (see above).
python tools/real-figure-scan/scan.py --reference tmp/adr-038-verification --scope all

# What CI runs: every test file and fixture in the working tree.
python tools/real-figure-scan/scan.py --denylist tools/real-figure-scan/denylist.txt

# Another branch, without checking it out.
python tools/real-figure-scan/scan.py --denylist tools/real-figure-scan/denylist.txt --rev origin/<branch>

# Docs (not in CI yet): --scope docs, or --scope all.
python tools/real-figure-scan/scan.py --denylist tools/real-figure-scan/denylist.txt --scope docs

# No real data at hand: list every non-round large number for review by hand.
python tools/real-figure-scan/scan.py --candidates --scope docs
```

Exit status: `0` PASS, `1` a real figure was found, `2` a usage or git error.
No `--reference` and no `--denylist` is an error, not a pass.

The default scope (`tests`) is `backend/tests/`, `quant/tests/`, all of
`frontend/src/` (its tests and the mock data beside them), any `fixtures/`
directory, and `tools/*/test_*`.

## Test

```bash
python -m unittest discover -s tools/real-figure-scan -p "test_*.py"
```

Every figure in the tests is invented.
