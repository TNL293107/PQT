"""Scan test files and fixtures for real vendor figures (ADR-041).

A fixture value must be invented, never copied from a real response
(``data-policy.md`` rules 1 and 3). This checks that mechanically: every
number of at least 10^9 in the files in scope, comments included, in every
form (plain, ``.0``, exponent such as ``1.234E13``, ``_`` / ``,`` / ``.``
thousands separators, C# suffixes such as ``m``), is compared against a set
of real figures. A single match fails. A round number (one significant
digit, such as ``1.0E11``) is exempt: it collides by chance, not by copying.

The real figures come from one of two places, never from the repository:

* ``--reference DIR``: real responses held in a git-ignored directory such
  as ``tmp/adr-038-verification/``. Every large number in every file there.
* ``--denylist FILE``: salted scrypt digests of real figures, made with
  ``--make-denylist``. A digest does not reveal its figure, so the file can be
  committed and the check can run in CI, where no real data exists.

What is scanned:

* ``--diff BASE HEAD``: the lines HEAD adds since its merge base with BASE.
* ``--rev REV`` (default: the working tree): every line of every file in scope.

A hit is reported as ``path:line`` and the figure's digit count. The figure
itself is never printed, because CI logs of a public repository are public.

Usage:
    python tools/real-figure-scan/scan.py --reference tmp/adr-038-verification \\
        --diff origin/develop HEAD
    python tools/real-figure-scan/scan.py --denylist tools/real-figure-scan/denylist.txt
    python tools/real-figure-scan/scan.py --denylist ... --scope docs
    python tools/real-figure-scan/scan.py --reference DIR --make-denylist OUT

Exit status: 0 PASS, 1 a real figure was found, 2 usage or git error.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import io
import re
import subprocess
import sys
from collections.abc import Callable, Iterable, Iterator
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from pathlib import Path

THRESHOLD = Decimal(10) ** 9

# No vendor figure has more than 31 digits; a larger number (1e5000, a long
# hash) is skipped with a warning rather than stopping the run.
MAX_EXPONENT = 30

# What "test files and fixtures" means, as fnmatch patterns on repo paths
# ('*' also matches '/').
SCOPES: dict[str, tuple[str, ...]] = {
    "tests": (
        "backend/tests/*",
        "quant/tests/*",
        "frontend/src/*",  # tests, and the mock data beside them
        "*fixtures/*",
        "tools/*/test_*",
    ),
    "docs": ("docs/*", "*.md"),
}
SCOPES["all"] = SCOPES["tests"] + SCOPES["docs"]

_BINARY_SUFFIXES = frozenset(
    {".parquet", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".gz", ".dll", ".exe"}
)

# Thousands separators other than '_', ',' and '.': space, no-break space,
# narrow no-break space, and the apostrophes some locales use.
_GROUP_SEPARATORS = " \u00a0\u202f'\u2019"

# A number token. Thousands separators are only taken as such when every
# group after the first has exactly three digits, so "405161,2025" is two
# numbers, not one. Exponents may carry a sign; C#/Java suffixes and the JS
# BigInt 'n' are allowed, and so is a currency glued on (VND, đ, ₫).
_NUMBER = re.compile(
    r"""
    (?<![\w.])                       # starts a token; embedded runs are read separately
    (?P<mantissa>
        \d{1,3}(?:_\d{3})+(?:\.\d+)? # 1_234_567, 1_234_567.5
      | \d{1,3}(?:,\d{3})+(?!\d)(?:\.\d+)? # 1,234,567, 1,234,567.5
      | \d{1,3}(?:\.\d{3}){2,}(?![\d.]) # 1.234.567 (Vietnamese style)
      | \d{1,3}(?:[ \u00a0\u202f'\u2019]\d{3}){2,}(?!\d)(?:\.\d+)? # 1 234 567, 1'234'567.5
      | \d+(?:_\d+)*(?:\.\d+)?       # 1234567, 1234.5, 1_2345
      | \.\d+
    )
    (?P<exponent>[eE][+-]?\d+)?
    (?P<suffix>[mMdDfFlLn]|[uU][lL]|[lL][uU])?
    (?:VND|vnd)?
    (?![0-9A-Za-z_])                 # a non-ASCII letter (đ, ₫) may follow
    """,
    re.VERBOSE,
)

# A UUID is masked with spaces of the same length before tokenising, so its
# segments (…d6-4e41-…, …-c01122334455) are never read and columns stay put.
# A hyphen is otherwise ordinary: VCB-12,345,678,901,234 is read whole.
_UUID = re.compile(r"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")

# A number anywhere, e.g. inside an identifier such as
# Revenue_12345678901234_test, after a dot, or glued on as value1.2345E13.
# Read only where no token covers it. An exponent counts only after a
# fraction, so a hex run such as ab4729e12cd is not read as 4.729E15.
_EMBEDDED = re.compile(r"\d+(?:\.\d+(?:[eE][+-]?\d+)?)?")

_DIGEST_SALT = b"pqt-real-figure-scan/1"
_DIGEST_HEADER = "# pqt real-figure-scan denylist v1: scrypt(n=4096,r=8,p=1,dklen=16), one hex digest per line"


@dataclass(frozen=True)
class Hit:
    """A real figure found in a file in scope."""

    path: str
    line: int
    digits: int


def canonical(token: str) -> str | None:
    """Return a number token's canonical value if it is at least 10^9, else None.

    The canonical form is the absolute value as a plain decimal string with no
    trailing fractional zeros, so ``74_000.0``, ``7.4E4`` and ``74000m`` agree.
    """
    match = _NUMBER.fullmatch(token)
    if match is None:
        return None
    return _canonical_from(match)


class _Oversized(str):
    """A number too large to be a figure; skipped, and the caller may warn."""


def _canonical_from(match: re.Match[str]) -> str | None:
    value = _value_from(match)
    return None if isinstance(value, _Oversized) else value


def _value_from(match: re.Match[str]) -> str | None:
    mantissa = match.group("mantissa")
    separators = "_," + _GROUP_SEPARATORS
    if any(separator in mantissa for separator in separators):
        mantissa = mantissa.translate({ord(separator): None for separator in separators})
    elif mantissa.count(".") >= 2:
        mantissa = mantissa.replace(".", "")
    text = mantissa + (match.group("exponent") or "")
    try:
        # copy_abs, not abs(): no context, so no Overflow on a huge exponent.
        value = Decimal(text).copy_abs()
    except InvalidOperation:
        return _Oversized(match.group())
    if value.adjusted() > MAX_EXPONENT:
        # Checked before any int() so 1e100000000 never builds a 10^8-digit int.
        # A long digit string with no exponent is a hash or an id: skipped quietly.
        return _Oversized(match.group()) if match.group("exponent") else None
    return _canonical_value(value)


def _canonical_value(value: Decimal) -> str | None:
    if value < THRESHOLD:
        return None
    if value == value.to_integral_value():
        return str(int(value))
    return format(value.normalize(), "f")


def is_round(value: str) -> bool:
    """True when a canonical value has a single significant digit."""
    significant = value.replace(".", "").strip("0")
    return len(significant) <= 1


def numbers(text: str, on_skip: Callable[[str], None] | None = None) -> Iterator[str]:
    """Yield the canonical value of every number of at least 10^9 in a text.

    Tokens first; then any run of ten or more digits no token covers, such as
    one inside an identifier, so nothing is read twice. A number above
    10^MAX_EXPONENT is no figure: it is skipped, and ``on_skip`` is told.
    """
    text = _UUID.sub(lambda uuid: " " * len(uuid.group()), text)
    covered: list[tuple[int, int]] = []
    for match in _NUMBER.finditer(text):
        covered.append(match.span())
        value = _value_from(match)
        if isinstance(value, _Oversized):
            if on_skip is not None:
                on_skip(value)
        elif value is not None:
            yield value
    for run in _EMBEDDED.finditer(text):
        start, end = run.span()
        if any(left <= start and end <= right for left, right in covered):
            continue
        value = _embedded_value(run.group())
        if value is not None:
            yield value


def _embedded_value(text: str) -> str | None:
    """A glued number's canonical value; one above 10^MAX_EXPONENT is skipped quietly."""
    try:
        value = Decimal(text).copy_abs()
    except InvalidOperation:
        return None
    if value.adjusted() > MAX_EXPONENT:
        return None
    return _canonical_value(value)


def digest(value: str) -> str:
    """The salted, deliberately slow digest a denylist holds for one figure."""
    return hashlib.scrypt(
        value.encode("ascii"), salt=_DIGEST_SALT, n=4096, r=8, p=1, dklen=16
    ).hex()


class Figures:
    """The set of real figures a scan compares against."""

    def __init__(self, plain: Iterable[str] = (), digests: Iterable[str] = ()) -> None:
        self._plain = {value for value in plain if not is_round(value)}
        self._digests = set(digests)
        self._cache: dict[str, bool] = {}

    def __len__(self) -> int:
        return len(self._plain) + len(self._digests)

    def __contains__(self, value: object) -> bool:
        if not isinstance(value, str) or is_round(value):
            return False
        if value in self._plain:
            return True
        if not self._digests:
            return False
        if value not in self._cache:
            self._cache[value] = digest(value) in self._digests
        return self._cache[value]

    @classmethod
    def from_reference(cls, root: Path) -> Figures:
        """Every large number in every file under a directory of real data."""
        found: set[str] = set()
        for path in sorted(root.rglob("*")):
            if path.is_file() and path.suffix.lower() not in _BINARY_SUFFIXES:
                found.update(numbers(path.read_text(encoding="utf-8", errors="ignore")))
        return cls(plain=found)

    @classmethod
    def from_denylist(cls, path: Path) -> Figures:
        """The digests in a denylist file; '#' starts a comment."""
        digests = []
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            line = raw.split("#", 1)[0].strip()
            if line:
                # Never echo the line: a figure pasted by mistake would reach a public CI log.
                if not re.fullmatch(r"[0-9a-f]{32}", line):
                    raise ValueError(f"{path}:{number}: not a 32-hex digest.")
                digests.append(line)
        return cls(digests=digests)

    def plain_values(self) -> set[str]:
        """The plain figures, for writing a denylist."""
        return set(self._plain)

    def digest_values(self) -> set[str]:
        """The denylist digests."""
        return set(self._digests)


class Candidates(Figures):
    """Every non-round number of at least 10^9: what a reviewer must look at by hand."""

    def __len__(self) -> int:
        return 0

    def __contains__(self, value: object) -> bool:
        return isinstance(value, str) and not is_round(value)


def in_scope(path: str, patterns: Iterable[str]) -> bool:
    """True when a repo path matches a scope and is not binary."""
    if Path(path).suffix.lower() in _BINARY_SUFFIXES:
        return False
    return any(fnmatch.fnmatchcase(path, pattern) for pattern in patterns)


def scan_lines(path: str, lines: Iterable[tuple[int, str]], figures: Figures) -> Iterator[Hit]:
    """Yield a hit for every real figure on the given (line number, text) pairs."""
    for number, text in lines:

        def warn(_: str, line: int = number) -> None:
            print(f"warning: {path}:{line}: skipped a number above 10^{MAX_EXPONENT}; not a figure", file=sys.stderr)

        for value in numbers(text, warn):
            if value in figures:
                yield Hit(path, number, len(value.split(".", 1)[0]))


def _git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        # quotepath off: a non-ASCII path comes back as itself, not "\303\220..." (QA L3).
        ["git", "-C", str(repo), "-c", "core.quotepath=off", *args],
        capture_output=True,
        check=False,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)}: {result.stderr.strip()}")
    return result.stdout


_HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")


def added_lines(diff: str) -> dict[str, list[tuple[int, str]]]:
    """The added lines of a unified diff (``-U0`` or more), by new path."""
    added: dict[str, list[tuple[int, str]]] = {}
    path: str | None = None
    line = 0
    for raw in diff.splitlines():
        if raw.startswith("+++ "):
            target = raw[4:].rstrip("\t")  # git adds a tab after a path with a space
            path = None if target == "/dev/null" else target.removeprefix("b/")
            continue
        hunk = _HUNK.match(raw)
        if hunk:
            line = int(hunk.group(1))
            continue
        if path is None or raw.startswith(("--- ", "diff ", "index ")):
            continue
        if raw.startswith("+"):
            added.setdefault(path, []).append((line, raw[1:]))
            line += 1
        elif raw.startswith(" "):
            line += 1
    return added


def scan_diff(repo: Path, base: str, head: str, patterns: tuple[str, ...], figures: Figures) -> tuple[int, list[Hit]]:
    """Scan the lines HEAD adds since its merge base with BASE."""
    diff = _git(repo, "diff", "-U0", "--no-color", "--no-ext-diff", "--no-renames", f"{base}...{head}")
    files = {path: lines for path, lines in added_lines(diff).items() if in_scope(path, patterns)}
    hits = [hit for path, lines in sorted(files.items()) for hit in scan_lines(path, lines, figures)]
    return len(files), hits


def scan_tree(repo: Path, rev: str | None, patterns: tuple[str, ...], figures: Figures) -> tuple[int, list[Hit]]:
    """Scan every line of every file in scope, at a revision or in the working tree."""
    listing = _git(repo, "ls-tree", "-r", "-z", "--name-only", rev) if rev else _git(repo, "ls-files", "-z")
    paths = [path for path in listing.split("\0") if path and in_scope(path, patterns)]
    hits: list[Hit] = []
    for path in paths:
        if rev:
            text = _git(repo, "show", f"{rev}:{path}")
        else:
            file = repo / path
            if not file.is_file():
                continue
            text = file.read_text(encoding="utf-8", errors="replace")
        hits.extend(scan_lines(path, enumerate(text.splitlines(), start=1), figures))
    return len(paths), hits


def write_denylist(figures: Figures, out: Path) -> int:
    """Write the digests of a reference's figures; returns how many."""
    digests = sorted(digest(value) for value in figures.plain_values())
    out.write_text("\n".join([_DIGEST_HEADER, *digests]) + "\n", encoding="utf-8")
    return len(digests)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="the repository (default: .)")
    source = parser.add_argument_group("real figures (at least one)")
    source.add_argument("--reference", type=Path, action="append", default=[], help="directory of real data")
    source.add_argument("--denylist", type=Path, action="append", default=[], help="digest file")
    target = parser.add_mutually_exclusive_group()
    target.add_argument("--diff", nargs=2, metavar=("BASE", "HEAD"), help="scan only the lines HEAD adds")
    target.add_argument("--rev", help="scan every file in scope at this revision")
    target.add_argument("--make-denylist", type=Path, metavar="OUT", help="write the references' digests and stop")
    parser.add_argument("--scope", choices=sorted(SCOPES), default="tests", help="which files (default: tests)")
    parser.add_argument(
        "--candidates",
        action="store_true",
        help="list every non-round number >= 10^9 for review by hand, without comparing (always exit 0)",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    """Run the scan; see the module docstring."""
    # A non-ASCII path must not crash a cp1252 console mid-report.
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, io.TextIOWrapper):
            stream.reconfigure(errors="backslashreplace")
    args = _parser().parse_args(argv)
    if args.candidates:
        return _list_candidates(args)
    if not args.reference and not args.denylist:
        print("error: give --reference DIR or --denylist FILE; no figures, no check.", file=sys.stderr)
        return 2

    try:
        plain: set[str] = set()
        for root in args.reference:
            if not root.is_dir():
                raise ValueError(f"--reference {root} is not a directory.")
            plain |= Figures.from_reference(root).plain_values()
        digests: set[str] = set()
        for path in args.denylist:
            digests |= Figures.from_denylist(path).digest_values()
        figures = Figures(plain=plain, digests=digests)

        if args.make_denylist:
            count = write_denylist(Figures(plain=plain), args.make_denylist)
            print(f"wrote {count} digest(s) to {args.make_denylist}")
            return 0

        patterns = SCOPES[args.scope]
        if args.diff:
            files, hits = scan_diff(args.repo, args.diff[0], args.diff[1], patterns, figures)
        else:
            files, hits = scan_tree(args.repo, args.rev, patterns, figures)
    except (RuntimeError, ValueError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    for hit in hits:
        print(f"{hit.path}:{hit.line}: a real figure ({hit.digits} digits)")
    verdict = "FAIL" if hits else "PASS"
    print(f"{verdict}: {len(hits)} real figure(s) in {files} file(s) [{args.scope}] against {len(figures)} figure(s)")
    return 1 if hits else 0


def _list_candidates(args: argparse.Namespace) -> int:
    patterns = SCOPES[args.scope]
    try:
        if args.diff:
            files, hits = scan_diff(args.repo, args.diff[0], args.diff[1], patterns, Candidates())
        else:
            files, hits = scan_tree(args.repo, args.rev, patterns, Candidates())
    except (RuntimeError, ValueError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    for hit in hits:
        print(f"{hit.path}:{hit.line}: a non-round number ({hit.digits} digits)")
    print(f"CANDIDATES: {len(hits)} in {files} file(s) [{args.scope}]; not compared, review by hand")
    return 0


if __name__ == "__main__":
    sys.exit(main())
