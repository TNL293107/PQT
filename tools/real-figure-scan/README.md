# Real-figure scanner

The check [ADR-041](../../docs/architecture/decisions/ADR-041-real-figures-in-a-test-fixture.md)
asks for: a test fixture must be invented, never copied from a real vendor
response (`docs/data-policy.md`, rules 1 and 3).

It reads every number of at least 10^9 in the files in scope, **comments
included**, in **every form** (plain, `.0`, exponent such as `1.234E13`,
`_` / `,` / `.` thousands separators, C# suffixes such as `m`), and compares it
with a set of real figures. One match fails. A round number (one significant
digit, such as `1.0E11`) is exempt, because it collides by chance.

Standard library only, Python 3.12 or later. Nothing to install.

## The real figures never enter git

| Source | What it is | Where it runs |
| --- | --- | --- |
| `--reference DIR` | Real responses in a git-ignored directory, e.g. `tmp/adr-038-verification/`. Every large number in every file | On the machine that holds the data |
| `--denylist FILE` | Salted scrypt digests of real figures, made with `--make-denylist`. A digest does not reveal its figure | Anywhere, including CI |

`denylist.txt` holds the digests of the **four** figures of the ADR-041
incident: the three the ADR names, and the equity-to-parent figure asserted in
`VendorStatementLinesTests.cs`, which the same fix commit (`9befc39`) replaced
and the Moderator ruled real on 2026-11-16.

**That is the whole reference set today.** The verification data the first
scanner read (`tmp/adr-038-verification/`, roughly 10,000 figures) was lost
with the old working copy, and the four were recovered from the numbers
`9befc39` removed. It is not being re-fetched until the legal review of the
Vietcap terms allows it (ADR-038). Until then the scan protects against these
four, not against any other real figure.

**Brute force.** A digest is salted and slow, but the space it hides is small:
a 13- to 14-digit figure whose rough size is known can be found by trying
candidates. This is accepted because the figures are a listed company's own
published statements; what ADR-041 prevents is the repository redistributing
the vendor's data, and a digest does not do that.

When real data is saved for a new verification, add its digests:

```bash
python tools/real-figure-scan/scan.py --reference tmp/<verification> --make-denylist tmp/new.txt
# append the digest lines of tmp/new.txt to tools/real-figure-scan/denylist.txt
```

A hit is printed as `path:line` and the figure's digit count, never the figure,
because the CI log of a public repository is public.

## Run

```bash
# Before pushing a fixture (ADR-041 rule 3): the lines your branch adds.
python tools/real-figure-scan/scan.py --reference tmp/adr-038-verification --diff origin/develop HEAD

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

The default scope (`tests`) is `backend/tests/`, `quant/tests/`, frontend
`*.test.*` and `src/test/`, any `fixtures/` directory, and `tools/*/test_*`.

## Test

```bash
python -m unittest discover -s tools/real-figure-scan -p "test_*.py"
```

Every figure in the tests is invented.
