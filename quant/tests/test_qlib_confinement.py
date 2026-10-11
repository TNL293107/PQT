"""The Qlib import guard (ADR-017), and the proof that it fires.

``import qlib`` may appear only under ``personal_quant/integrations/qlib/``.
So may ``import mlflow``: MLflow arrives with Qlib as its recorder's sink, and
ADR-017 keeps it inside the adapter, with PQT's experiment store canonical.
The boundary between "Qlib behind an adapter" and "Qlib in the middle" is
nearly invisible in the first stray import and nearly irreversible by the
tenth, so it is held by a check rather than by review.

The check reads the syntax tree, not the text: a docstring or comment that
mentions ``import qlib`` is not an import, and an import nested inside a
function, or made through :func:`importlib.import_module` or ``__import__``
with a literal name, is. It scans the package and the tests both — a test that
imports Qlib directly is a second door into the same room.

A guard that has never been seen to fire is not known to work, so the second
half of this file plants stray imports in a scratch tree and asserts that each
one is caught, and that the adapter's own directory is not.

Removing Qlib (``docs/architecture/qlib-integration.md``) deletes this file
with the adapter.
"""

from __future__ import annotations

import ast
from dataclasses import dataclass
from pathlib import Path

import pytest

QUANT_ROOT = Path(__file__).resolve().parents[1]
SCANNED = (QUANT_ROOT / "src", QUANT_ROOT / "tests")
ALLOWED = Path("personal_quant", "integrations", "qlib")
GUARDED = frozenset({"qlib", "mlflow"})

_DYNAMIC_IMPORTERS = frozenset({"import_module", "__import__"})


@dataclass(frozen=True, slots=True)
class StrayImport:
    """One import of a guarded package outside the adapter."""

    path: Path
    line: int
    module: str

    def __str__(self) -> str:
        """Render as ``path:line imports module``."""
        return f"{self.path}:{self.line} imports {self.module}"


def _is_guarded(module: str | None) -> bool:
    return module is not None and module.split(".")[0] in GUARDED


def _imports_of(tree: ast.AST) -> list[tuple[int, str]]:
    found: list[tuple[int, str]] = []

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            found += [(node.lineno, alias.name) for alias in node.names]
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            found.append((node.lineno, node.module))
        elif isinstance(node, ast.Call) and node.args:
            function = node.func
            name = function.attr if isinstance(function, ast.Attribute) else None
            name = function.id if isinstance(function, ast.Name) else name
            first = node.args[0]

            if (
                name in _DYNAMIC_IMPORTERS
                and isinstance(first, ast.Constant)
                and isinstance(first.value, str)
            ):
                found.append((node.lineno, first.value))

    return [(line, module) for line, module in found if _is_guarded(module)]


def _is_allowed(path: Path, root: Path) -> bool:
    parts = path.relative_to(root).parts
    allowed = ALLOWED.parts

    return any(parts[index : index + len(allowed)] == allowed for index in range(len(parts)))


def stray_imports(roots: tuple[Path, ...]) -> tuple[list[StrayImport], int]:
    """Find every import of Qlib or MLflow outside the adapter package.

    Args:
        roots: The directories to scan, recursively, for ``*.py`` files.

    Returns:
        The stray imports, and how many files were scanned — so a scan that
        silently looked at nothing cannot pass as a clean one.
    """
    strays: list[StrayImport] = []
    scanned = 0

    for root in roots:
        for path in sorted(root.rglob("*.py")):
            scanned += 1

            if _is_allowed(path, root):
                continue

            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            strays += [StrayImport(path, line, module) for line, module in _imports_of(tree)]

    return strays, scanned


def test_qlib_is_imported_only_inside_the_adapter() -> None:
    strays, scanned = stray_imports(SCANNED)

    assert scanned > 0, "The guard scanned no files; its roots are wrong."
    assert not strays, "Imported outside personal_quant/integrations/qlib:\n" + "\n".join(
        str(stray) for stray in strays
    )


def test_the_guard_scans_the_adapter_it_exempts() -> None:
    # If the adapter moved, the exemption would silently cover nothing and a
    # renamed adapter would read as a stray; either way the guard is stale.
    adapter = QUANT_ROOT / "src" / ALLOWED

    assert (adapter / "__init__.py").is_file()
    assert any("qlib" in path.read_text(encoding="utf-8") for path in adapter.rglob("*.py"))


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
        "__import__('qlib')\n",
        "import mlflow\n",
        "from mlflow.tracking import MlflowClient\n",
    ],
)
def test_a_planted_import_is_caught(tmp_path: Path, source: str) -> None:
    planted = tmp_path / "personal_quant" / "research" / "stray.py"
    planted.parent.mkdir(parents=True)
    planted.write_text(source, encoding="utf-8")

    strays, scanned = stray_imports((tmp_path,))

    assert scanned == 1
    assert [stray.path for stray in strays] == [planted]
    assert strays[0].module.split(".")[0] in GUARDED


def test_the_adapter_directory_is_exempt(tmp_path: Path) -> None:
    adapter = tmp_path / "personal_quant" / "integrations" / "qlib" / "model.py"
    adapter.parent.mkdir(parents=True)
    adapter.write_text("import qlib\nfrom qlib.contrib.model.gbdt import LGBModel\n", "utf-8")

    assert stray_imports((tmp_path,)) == ([], 1)


def test_a_sibling_named_like_the_adapter_is_not_exempt(tmp_path: Path) -> None:
    # personal_quant/integrations/qlib_extras is not the adapter.
    sibling = tmp_path / "personal_quant" / "integrations" / "qlib_extras" / "helper.py"
    sibling.parent.mkdir(parents=True)
    sibling.write_text("import qlib\n", encoding="utf-8")

    strays, _ = stray_imports((tmp_path,))

    assert [stray.path for stray in strays] == [sibling]


@pytest.mark.parametrize(
    "source",
    [
        '"""Never write import qlib here."""\n',
        "# import qlib\n",
        "import qlibrary\n",
        "from qlibrary import thing\n",
        "import mlflowish\n",
        "from . import qlib\n",
        "name = 'qlib'\n",
    ],
)
def test_text_that_is_not_an_import_of_qlib_is_not_caught(tmp_path: Path, source: str) -> None:
    clean = tmp_path / "personal_quant" / "research" / "clean.py"
    clean.parent.mkdir(parents=True)
    clean.write_text(source, encoding="utf-8")

    assert stray_imports((tmp_path,)) == ([], 1)
