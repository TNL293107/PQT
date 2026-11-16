"""Tests for the ADR-041 real-figure scanner. Every figure here is invented."""

from __future__ import annotations

import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import scan

# An invented "real" figure and the forms a fixture could carry it in.
FIGURE = "12345678901234"
FORMS = [
    "12345678901234",
    "12345678901234.0",
    "12_345_678_901_234",
    "12,345,678,901,234",
    "12.345.678.901.234",
    "1.2345678901234E13",
    "1.2345678901234e+13",
    "12345678901234m",
    "12345678901234L",
    "-12345678901234",
    # QA's M1: forms that used to get through.
    "12345678901234n",
    "12345678901234đ",
    "12345678901234₫",
    "12345678901234VND",
    "12 345 678 901 234",
    "12 345 678 901 234",
    "12 345 678 901 234",
    "12'345'678'901'234",
    "12.345.678.901.234,00",
]

E = "e"  # glued into huge exponents at run time, see the L2 tests

# Where a figure sits inside other text (QA's M1): still read.
EMBEDDED = [
    "Revenue_12345678901234_test",
    "total.12345678901234",
    "id12345678901234",
]


# A throwaway repository must commit on any machine, including one that signs
# every commit by default (QA's L4).
_THROWAWAY = (
    "-c", "user.name=t",
    "-c", "user.email=t@example.invalid",
    "-c", "commit.gpgsign=false",
    "-c", "tag.gpgsign=false",
)


def _git(repo: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(repo), *_THROWAWAY, *args], check=True, capture_output=True)


class CanonicalTests(unittest.TestCase):
    def test_every_form_of_a_figure_has_the_same_canonical_value(self) -> None:
        for form in FORMS:
            with self.subTest(form=form):
                self.assertEqual([FIGURE], list(scan.numbers(f"x = {form};")))

    def test_a_number_below_ten_to_the_nine_is_ignored(self) -> None:
        self.assertEqual([], list(scan.numbers("999999999 and 9.99999999E8")))

    def test_a_decimal_figure_keeps_its_fraction(self) -> None:
        self.assertEqual(["1234567890.5"], list(scan.numbers("1234567890.50")))

    def test_a_list_separated_by_commas_is_two_numbers(self) -> None:
        self.assertEqual(["1234567891", "2345678912"], list(scan.numbers("[1234567891,2345678912]")))

    def test_a_figure_inside_an_identifier_or_after_a_dot_is_read(self) -> None:
        for text in EMBEDDED:
            with self.subTest(text=text):
                self.assertIn(FIGURE, list(scan.numbers(text)))

    def test_a_short_digit_run_inside_an_identifier_is_not_a_number(self) -> None:
        self.assertEqual([], list(scan.numbers("Test_123456789_case v1.2.3 bsa53 0x1F")))

    def test_a_figure_is_read_once_not_twice(self) -> None:
        self.assertEqual([FIGURE], list(scan.numbers("Revenue_12345678901234_test")))
        self.assertEqual([FIGURE], list(scan.numbers("x = 12_345_678_901_234;")))

    def test_a_huge_exponent_is_skipped_and_reported_not_raised(self) -> None:
        # QA's L2: an exponent of 5000 hit the int-string limit; one of 10^8 would build a 10^8-digit int.
        # Built at run time (E + "5000") so the scanner's own CI scan of this file stays quiet.
        skipped: list[str] = []
        found = list(scan.numbers(f"1{E}5000, 1{E}100000000, 9{E}99999999999 and {FIGURE}", skipped.append))
        self.assertEqual([FIGURE], found)
        self.assertEqual(3, len(skipped))

    def test_a_uuid_segment_is_not_an_exponent(self) -> None:
        skipped: list[str] = []
        self.assertEqual([], list(scan.numbers('"ae6813d6-1629-4e41-acc2-1f52ccff66c7"', skipped.append)))
        self.assertEqual([], skipped)

    def test_a_long_digit_string_without_an_exponent_is_a_hash_and_skipped_quietly(self) -> None:
        skipped: list[str] = []
        self.assertEqual([], list(scan.numbers("sha " + "5" * 64, skipped.append)))
        self.assertEqual([], skipped)

    def test_a_rescaled_figure_is_a_known_limit(self) -> None:
        # Written in millions, the figure is below 10^9 and is not read (README, "Limits").
        self.assertEqual([], list(scan.numbers("12,345,678.901234 million")))

    def test_a_round_number_is_exempt(self) -> None:
        self.assertTrue(scan.is_round("100000000000"))
        self.assertTrue(scan.is_round(scan.canonical("1.0E11") or ""))
        self.assertFalse(scan.is_round("110000000000"))


class FiguresTests(unittest.TestCase):
    def test_a_round_reference_figure_never_matches(self) -> None:
        figures = scan.Figures(plain=["100000000000", FIGURE])
        self.assertNotIn("100000000000", figures)
        self.assertIn(FIGURE, figures)

    def test_a_denylist_matches_the_figure_it_was_made_from_and_does_not_contain_it(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            reference = Path(tmp, "real")
            reference.mkdir()
            Path(reference, "response.json").write_text('{"bsa53": 1.2345678901234E13}', encoding="utf-8")
            out = Path(tmp, "denylist.txt")

            with contextlib.redirect_stdout(io.StringIO()):
                code = scan.main(["--reference", str(reference), "--make-denylist", str(out)])
            self.assertEqual(0, code)
            text = out.read_text(encoding="utf-8")
            figures = scan.Figures.from_denylist(out)

        self.assertNotIn(FIGURE, text)
        self.assertIn(FIGURE, figures)
        self.assertNotIn("23456789012345", figures)

    def test_a_malformed_denylist_line_is_reported_by_number_never_echoed(self) -> None:
        # Someone pastes a figure instead of its digest (QA's L1): the public log must not show it.
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "denylist.txt")
            path.write_text(f"# header\n{FIGURE}\n", encoding="utf-8")
            err = io.StringIO()
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
                code = scan.main(["--repo", tmp, "--denylist", str(path)])

        self.assertEqual(2, code)
        self.assertIn("denylist.txt:2", err.getvalue())
        self.assertNotIn(FIGURE, err.getvalue())

    def test_a_malformed_denylist_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "denylist.txt")
            path.write_text("not-a-digest\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                scan.Figures.from_denylist(path)


class DiffTests(unittest.TestCase):
    def test_only_added_lines_are_read_with_their_new_line_numbers(self) -> None:
        diff = (
            "diff --git a/backend/tests/A.cs b/backend/tests/A.cs\n"
            "--- a/backend/tests/A.cs\n"
            "+++ b/backend/tests/A.cs\n"
            "@@ -3,0 +4,2 @@\n"
            "+first\n"
            "+second\n"
            "@@ -9 +11 @@\n"
            "-old\n"
            "+third"
        )
        self.assertEqual(
            {"backend/tests/A.cs": [(4, "first"), (5, "second"), (11, "third")]},
            scan.added_lines(diff),
        )

    def test_a_deleted_file_adds_nothing(self) -> None:
        diff = "--- a/backend/tests/A.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-gone"
        self.assertEqual({}, scan.added_lines(diff))


class ScopeTests(unittest.TestCase):
    def test_tests_and_fixtures_are_in_the_default_scope_and_docs_are_not(self) -> None:
        tests = scan.SCOPES["tests"]
        self.assertTrue(scan.in_scope("backend/tests/PersonalQuant.UnitTests/X.cs", tests))
        self.assertTrue(scan.in_scope("quant/tests/fixtures/jobs/a.json", tests))
        self.assertTrue(scan.in_scope("data/fixtures/instruments.csv", tests))
        self.assertTrue(scan.in_scope("frontend/src/lib/x.test.ts", tests))
        self.assertFalse(scan.in_scope("backend/src/X.cs", tests))
        self.assertFalse(scan.in_scope("docs/architecture/decisions/ADR-1.md", tests))
        self.assertTrue(scan.in_scope("docs/architecture/decisions/ADR-1.md", scan.SCOPES["docs"]))
        self.assertFalse(scan.in_scope("quant/tests/fixtures/a.parquet", tests))


class EndToEndTests(unittest.TestCase):
    """A throwaway repository: a base commit, then a head that adds a figure in a comment."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        root = Path(self._tmp.name)
        self.repo = root / "repo"
        self.repo.mkdir()
        self.reference = root / "real"
        self.reference.mkdir()
        Path(self.reference, "q1.json").write_text(f'{{"isa3": {FIGURE}}}', encoding="utf-8")

        _git(self.repo, "init", "-q", "-b", "main")
        tests = self.repo / "backend" / "tests"
        tests.mkdir(parents=True)
        (tests / "A.cs").write_text("var a = 70000000000000;\n", encoding="utf-8")
        (self.repo / "docs").mkdir()
        (self.repo / "docs" / "note.md").write_text(f"Revenue was {FIGURE}.\n", encoding="utf-8")
        _git(self.repo, "add", ".")
        _git(self.repo, "commit", "-q", "-m", "base")
        _git(self.repo, "tag", "base")
        (tests / "A.cs").write_text(
            "var a = 70000000000000;\n/// <c>1.2345678901234E13</c>\nvar b = 23456789012345;\n",
            encoding="utf-8",
        )
        _git(self.repo, "commit", "-q", "-am", "head")

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def _run(self, *args: str) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = scan.main(["--repo", str(self.repo), "--reference", str(self.reference), *args])
        return code, out.getvalue()

    def test_a_figure_added_in_a_comment_in_exponent_form_fails_the_diff(self) -> None:
        code, out = self._run("--diff", "base", "HEAD")
        self.assertEqual(1, code)
        self.assertIn("backend/tests/A.cs:2: a real figure (14 digits)", out)
        self.assertIn("FAIL: 1 real figure(s)", out)

    def test_the_figure_itself_is_never_printed(self) -> None:
        _, out = self._run("--diff", "base", "HEAD")
        self.assertNotIn(FIGURE, out)
        self.assertNotIn("1.2345678901234E13", out)

    def test_the_base_passes(self) -> None:
        code, out = self._run("--rev", "base")
        self.assertEqual(0, code)
        self.assertIn("PASS", out)

    def test_docs_are_read_only_when_asked(self) -> None:
        self.assertEqual(0, self._run("--rev", "base")[0])
        code, out = self._run("--rev", "base", "--scope", "docs")
        self.assertEqual(1, code)
        self.assertIn("docs/note.md:1", out)

    def test_the_working_tree_is_scanned_by_default(self) -> None:
        self.assertEqual(1, self._run()[0])

    def test_no_figures_is_a_usage_error_not_a_pass(self) -> None:
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(2, scan.main(["--repo", str(self.repo)]))

    def test_a_huge_exponent_in_a_file_warns_and_still_passes(self) -> None:
        (self.repo / "backend" / "tests" / "B.cs").write_text(f"var big = 1{E}5000;\n", encoding="utf-8")
        _git(self.repo, "add", ".")
        _git(self.repo, "commit", "-q", "-m", "big")
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = scan.main(["--repo", str(self.repo), "--reference", str(self.reference), "--rev", "HEAD"])

        self.assertEqual(1, code)  # A.cs:2 still fails; B.cs does not stop the run
        self.assertIn("backend/tests/B.cs:1: skipped", err.getvalue())
        self.assertIn("FAIL: 1 real figure(s)", out.getvalue())

    def test_a_non_ascii_path_is_scanned_in_every_mode(self) -> None:
        # QA's L3: git quotes such paths by default, and the file was skipped or failed.
        (self.repo / "backend" / "tests" / "Đơn giá.cs").write_text(f"var x = {FIGURE};\n", encoding="utf-8")
        _git(self.repo, "add", ".")
        _git(self.repo, "commit", "-q", "-m", "non-ascii")
        for mode in ((), ("--rev", "HEAD"), ("--diff", "base", "HEAD")):
            with self.subTest(mode=mode):
                code, out = self._run(*mode)
                self.assertEqual(1, code)
                self.assertIn("backend/tests/Đơn giá.cs:1: a real figure (14 digits)", out)

    def test_an_unknown_revision_is_an_error_not_a_pass(self) -> None:
        self.assertEqual(2, self._run("--rev", "no-such-rev")[0])

    def test_candidates_lists_non_round_numbers_without_comparing(self) -> None:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = scan.main(["--repo", str(self.repo), "--candidates"])
        self.assertEqual(0, code)
        self.assertIn("backend/tests/A.cs:2", out.getvalue())
        self.assertIn("backend/tests/A.cs:3", out.getvalue())
        self.assertNotIn("A.cs:1", out.getvalue())


class DenylistEndToEndTests(EndToEndTests):
    """The path CI runs (QA's M3): main() with --denylist only, no real data."""

    def setUp(self) -> None:
        super().setUp()
        self.denylist = Path(self._tmp.name, "denylist.txt")
        with contextlib.redirect_stdout(io.StringIO()):
            scan.main(["--reference", str(self.reference), "--make-denylist", str(self.denylist)])

    def _run(self, *args: str) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = scan.main(["--repo", str(self.repo), "--denylist", str(self.denylist), *args])
        return code, out.getvalue()

    def test_the_ci_command_fails_on_the_working_tree_and_does_not_print_the_figure(self) -> None:
        code, out = self._run()
        self.assertEqual(1, code)
        self.assertIn("backend/tests/A.cs:2: a real figure (14 digits)", out)
        self.assertIn("against 1 figure(s)", out)
        self.assertNotIn(FIGURE, out)
        self.assertNotIn(FIGURE, self.denylist.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
