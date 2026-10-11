"""The Qlib import guard (ADR-017), and the proof that it fires.

``import qlib`` may appear only under ``src/personal_quant/integrations/qlib/``.
So may ``import mlflow``: MLflow arrives with Qlib as its recorder's sink, and
ADR-017 keeps it inside the adapter, with PQT's experiment store canonical.
The boundary between "Qlib behind an adapter" and "Qlib in the middle" is
nearly invisible in the first stray import and nearly irreversible by the
tenth, so it is held by a check rather than by review.

The check reads the syntax tree, not the text, and flags three things outside
the adapter:

* an ``import`` or ``from … import`` of a guarded package, nested in a
  function or not;
* any string literal that *is* a guarded name — ``"qlib"``, ``"mlflow"``, or
  one starting ``"qlib."`` or ``"mlflow."``. That covers every way of handing a
  literal name to an importer without having to enumerate the importers:
  :func:`importlib.import_module` under any alias, positionally or by keyword,
  ``__import__``, :func:`importlib.util.find_spec`, a name kept in a constant
  and passed along later;
* any string literal, other than a docstring, that parses as Python which
  itself does one of the above — ``exec("import qlib")``, or source handed to
  ``python -c``.

A docstring or comment that mentions ``import qlib`` is not an import, and a
name such as ``qlibrary`` or ``pyqlib`` is not a guarded one. Two files are
allowed to *name* the guarded packages without importing them: this one, which
must spell out what it looks for, and ``test_qlib_optional.py``, which runs
interpreters that check the packages were not loaded. Both are still scanned
for import statements.

What the guard does not catch is a name computed at run time — ``"q" + "lib"``,
a name read from a file or the environment, source assembled before it is
executed. Closing that would mean evaluating the program. The guard exists to
catch accidents, which are literal; review catches intent.

A guard that has never been seen to fire is not known to work, so the second
half of this file plants stray imports in a scratch tree and asserts that each
one is caught, and that the adapter's own directory — and only that
directory, at that place — is not.

Removing Qlib (``docs/architecture/qlib-integration.md``) deletes this file
with the adapter.
"""

from __future__ import annotations

import ast
import textwrap
from dataclasses import dataclass
from pathlib import Path

import pytest

QUANT_ROOT = Path(__file__).resolve().parents[1]
SCANNED = ("src", "tests")
ADAPTER = Path("src", "personal_quant", "integrations", "qlib")
GUARDED = frozenset({"qlib", "mlflow"})
MAY_NAME_GUARDED = frozenset(
    {Path("tests", "test_qlib_confinement.py"), Path("tests", "test_qlib_optional.py")}
)


@dataclass(frozen=True, slots=True)
class StrayImport:
    """One import, or literal name, of a guarded package outside the adapter."""

    path: Path
    line: int
    module: str
    how: str = "imports"

    def __str__(self) -> str:
        """Render as ``path:line imports module``."""
        return f"{self.path}:{self.line} {self.how} {self.module}"


def _is_guarded(module: str | None) -> bool:
    return module is not None and module.split(".")[0] in GUARDED


def _names_guarded(text: str) -> bool:
    # The literal is a guarded package or one of its modules, not a string
    # that merely contains the word: "pyqlib" and "qlib-scratch" are not.
    return any(text == name or text.startswith(f"{name}.") for name in GUARDED)


def _docstrings(tree: ast.AST) -> set[int]:
    found: set[int] = set()

    for node in ast.walk(tree):
        if (
            isinstance(node, ast.Module | ast.ClassDef | ast.FunctionDef | ast.AsyncFunctionDef)
            and node.body
        ):
            first = node.body[0]

            if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant):
                found.add(id(first.value))

    return found


def _embedded_source(text: str) -> ast.AST | None:
    if not any(name in text for name in GUARDED):
        return None

    try:
        return ast.parse(textwrap.dedent(text))
    except SyntaxError:
        return None


def _imports_of(tree: ast.AST, *, may_name_guarded: bool = False) -> list[StrayImport]:
    found: list[StrayImport] = []
    docstrings = _docstrings(tree)

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            found += [StrayImport(Path(), node.lineno, alias.name) for alias in node.names]
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            found.append(StrayImport(Path(), node.lineno, node.module))
        elif (
            isinstance(node, ast.Constant) and isinstance(node.value, str) and not may_name_guarded
        ):
            if _names_guarded(node.value):
                found.append(StrayImport(Path(), node.lineno, node.value, "names"))
            elif id(node) not in docstrings and (embedded := _embedded_source(node.value)):
                found += [
                    StrayImport(Path(), node.lineno, inner.module, "executes source importing")
                    for inner in _imports_of(embedded)
                ]

    return [stray for stray in found if _is_guarded(stray.module)]


def _is_adapter(relative: Path) -> bool:
    # Anchored: the adapter is src/personal_quant/integrations/qlib under the
    # quant root, and nothing else. A directory of the same name elsewhere —
    # under tests/, or nested deeper in src/ — is not exempt.
    return relative.parts[: len(ADAPTER.parts)] == ADAPTER.parts


def stray_imports(root: Path) -> tuple[list[StrayImport], int]:
    """Find every import of Qlib or MLflow outside the adapter package.

    Args:
        root: The quant root; its ``src`` and ``tests`` are scanned,
            recursively, for ``*.py`` files.

    Returns:
        The stray imports, and how many files were scanned — so a scan that
        silently looked at nothing cannot pass as a clean one.
    """
    strays: list[StrayImport] = []
    scanned = 0

    for top in SCANNED:
        for path in sorted((root / top).rglob("*.py")):
            scanned += 1
            relative = path.relative_to(root)

            if _is_adapter(relative):
                continue

            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            strays += [
                StrayImport(path, stray.line, stray.module, stray.how)
                for stray in _imports_of(tree, may_name_guarded=relative in MAY_NAME_GUARDED)
            ]

    return strays, scanned


def test_qlib_is_imported_only_inside_the_adapter() -> None:
    strays, scanned = stray_imports(QUANT_ROOT)

    assert scanned > 0, "The guard scanned no files; its roots are wrong."
    assert not strays, "Outside src/personal_quant/integrations/qlib:\n" + "\n".join(
        str(stray) for stray in strays
    )


def test_the_guard_scans_the_adapter_it_exempts() -> None:
    # If the adapter moved, the exemption would silently cover nothing and a
    # renamed adapter would read as a stray; either way the guard is stale.
    adapter = QUANT_ROOT / ADAPTER

    assert (adapter / "__init__.py").is_file()
    assert any("qlib" in path.read_text(encoding="utf-8") for path in adapter.rglob("*.py"))
    assert all((QUANT_ROOT / allowed).is_file() for allowed in MAY_NAME_GUARDED)


def _plant(root: Path, relative: str, source: str) -> Path:
    planted = root / relative
    planted.parent.mkdir(parents=True, exist_ok=True)
    planted.write_text(source, encoding="utf-8")

    return planted


@pytest.mark.parametrize(
    "source",
    [
        "import qlib\n",
        "import qlib.data as data\n",
        "import os, qlib\n",
        "from qlib.contrib.model.gbdt import LGBModel\n",
        "from qlib import init\n",
        "def later() -> None:\n    import qlib.workflow\n",
        "import importlib\nimportlib.import_module('qlib.contrib')\n",
        "from importlib import import_module\nimport_module('qlib')\n",
        "from importlib import import_module as im\nim('qlib')\n",
        "import importlib\nimportlib.import_module(name='qlib')\n",
        "import importlib.util\nimportlib.util.find_spec('mlflow')\n",
        "__import__('qlib')\n",
        "ENGINE = 'qlib'\n",
        "MODULE = 'mlflow.tracking'\n",
        "name = 'q' 'lib'\n",
        "part = 'workflow'\nname = f'qlib.{part}'\n",
        "exec('import qlib')\n",
        "exec('from mlflow.tracking import MlflowClient')\n",
        "source = 'import qlib.workflow'\nexec(source)\n",
        'exec("""\n    import qlib\n""")\n',
        "import sys\nargv = [sys.executable, '-c', 'import mlflow']\n",
        "import mlflow\n",
        "from mlflow.tracking import MlflowClient\n",
    ],
)
def test_a_planted_import_is_caught(tmp_path: Path, source: str) -> None:
    planted = _plant(tmp_path, "src/personal_quant/research/stray.py", source)

    strays, scanned = stray_imports(tmp_path)

    assert scanned == 1
    assert [stray.path for stray in strays] == [planted]
    assert strays[0].module.split(".")[0] in GUARDED


def test_a_planted_import_in_the_tests_is_caught(tmp_path: Path) -> None:
    planted = _plant(tmp_path, "tests/test_stray.py", "import qlib\n")

    strays, _ = stray_imports(tmp_path)

    assert [stray.path for stray in strays] == [planted]


def test_the_adapter_directory_is_exempt(tmp_path: Path) -> None:
    _plant(
        tmp_path,
        "src/personal_quant/integrations/qlib/model.py",
        "import qlib\nfrom qlib.contrib.model.gbdt import LGBModel\nENGINE = 'qlib'\n",
    )

    assert stray_imports(tmp_path) == ([], 1)


@pytest.mark.parametrize(
    "relative",
    [
        # Not the adapter: a sibling named like it, a copy of its path under
        # tests/, and one nested further down src/.
        "src/personal_quant/integrations/qlib_extras/helper.py",
        "tests/personal_quant/integrations/qlib/helper.py",
        "src/vendored/personal_quant/integrations/qlib/helper.py",
    ],
)
def test_only_the_adapter_at_its_place_is_exempt(tmp_path: Path, relative: str) -> None:
    planted = _plant(tmp_path, relative, "import qlib\n")

    strays, _ = stray_imports(tmp_path)

    assert [stray.path for stray in strays] == [planted]


def test_the_files_that_may_name_qlib_may_not_import_it(tmp_path: Path) -> None:
    named = _plant(tmp_path, "tests/test_qlib_optional.py", "ENGINES = {'qlib', 'mlflow'}\n")
    imported = _plant(tmp_path, "tests/test_qlib_confinement.py", "import qlib\n")

    strays, scanned = stray_imports(tmp_path)

    assert scanned == 2
    assert named not in [stray.path for stray in strays]
    assert [stray.path for stray in strays] == [imported]


@pytest.mark.parametrize(
    "source",
    [
        '"""Never write import qlib here."""\n',
        '"""import qlib"""\n',
        "# import qlib\n",
        "import qlibrary\n",
        "from qlibrary import thing\n",
        "import mlflowish\n",
        "from . import qlib\n",
        "name = 'qlibrary'\n",
        "distribution = 'pyqlib'\n",
        "folder = 'qlib-scratch'\n",
        "adapter = 'personal_quant.integrations.qlib'\n",
        "reason = 'Qlib is not installed'\n",
    ],
)
def test_text_that_is_not_an_import_of_qlib_is_not_caught(tmp_path: Path, source: str) -> None:
    _plant(tmp_path, "src/personal_quant/research/clean.py", source)

    assert stray_imports(tmp_path) == ([], 1)


def test_a_name_computed_at_run_time_is_the_documented_residual(tmp_path: Path) -> None:
    # Recorded, not endorsed: the guard reads literals, and this name is not
    # one until the program runs. See the module docstring.
    _plant(
        tmp_path,
        "src/personal_quant/research/evasive.py",
        "import importlib\nimportlib.import_module('q' + 'lib')\n",
    )

    assert stray_imports(tmp_path) == ([], 1)
