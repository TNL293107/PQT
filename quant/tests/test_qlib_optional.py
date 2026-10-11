"""Qlib is optional: PQT runs without it, and the adapter says so plainly (ADR-017).

These run in every environment, with or without the ``research`` extra. The
adapter's behaviour with Qlib present is in ``test_qlib_adapter.py``, which
skips without it.

Absence is not taken on trust from the environment the suite happens to run
in. A fresh interpreter has Qlib made unimportable — ``sys.modules["qlib"]``
set to ``None``, which is how Python itself marks a module as unavailable — and
every module in the package is imported there; a second interpreter imports
them all with Qlib importable and checks that none of them loaded it, MLflow,
LightGBM or pandas; a third imports the adapter alone and checks it loaded
none of those or NumPy. (NumPy is loaded by the package as a whole when it is
installed, by pyarrow, which reads PQT's datasets; that is not the adapter's
doing and not Qlib's.)
"""

from __future__ import annotations

import inspect
import os
import stat
import subprocess
import sys
import textwrap
import typing
from collections.abc import Callable, Sequence
from pathlib import Path
from typing import Any, cast

import pytest

from personal_quant.integrations import qlib as adapter
from personal_quant.integrations.qlib import LightGBMSettings, QlibLightGBM, QlibUnavailableError
from personal_quant.research.protocols import Model

SOURCE = Path(__file__).resolve().parents[1] / "src"
ENGINE_PACKAGES = frozenset({"qlib", "mlflow", "lightgbm", "pandas", "numpy"})
# Which of those importing the whole package may load: NumPy, through pyarrow.
LOADED_BY_THE_PACKAGE = frozenset({"numpy"})

_ENGINES_LOADED = """
print(",".join(sorted({m.split(".")[0] for m in sys.modules} & set(ENGINE_PACKAGES))))
"""

_IMPORT_EVERYTHING = """
import importlib
import pkgutil
import sys

import personal_quant

names = sorted(
    module.name
    for module in pkgutil.walk_packages(personal_quant.__path__, "personal_quant.")
    if not module.name.endswith("__main__")
)
for name in names:
    importlib.import_module(name)
"""


def _run(code: str) -> str:
    environment = {**os.environ, "PYTHONPATH": str(SOURCE)}
    completed = subprocess.run(  # noqa: S603 - the interpreter running these tests.
        [sys.executable, "-c", textwrap.dedent(code)],
        capture_output=True,
        text=True,
        env=environment,
        check=False,
        timeout=120,
    )

    assert completed.returncode == 0, completed.stderr

    return completed.stdout.strip()


def _engines(prefix: str) -> set[str]:
    # The last line printed, which is empty when no engine was loaded.
    loaded = _run(
        prefix + f"ENGINE_PACKAGES = {sorted(ENGINE_PACKAGES)!r}\n" + _ENGINES_LOADED
    ).rpartition("\n")[2]

    return set(loaded.split(",")) - {""}


def test_no_module_of_the_package_imports_qlib_on_import() -> None:
    count = _run(_IMPORT_EVERYTHING + "print(len(names))\n")
    engines = _engines(_IMPORT_EVERYTHING)

    assert int(count) > 20, "The walk imported too few modules to prove anything."
    assert engines <= LOADED_BY_THE_PACKAGE


def test_the_adapter_alone_loads_no_engine_on_import() -> None:
    engines = _engines(
        """
import sys

import personal_quant.integrations.qlib
import personal_quant.integrations.qlib.model
import personal_quant.integrations.qlib.session
"""
    )

    assert engines == set()


def test_the_package_works_with_qlib_absent() -> None:
    reported = _run(
        """
import sys

sys.modules["qlib"] = None
"""
        + _IMPORT_EVERYTHING
        + """
from personal_quant.integrations.qlib import (
    QlibLightGBM, QlibUnavailableError, available, engine_version,
)

assert not available()

for attempt in (engine_version, lambda: QlibLightGBM().fit([[1.0], [2.0]], [1.0, 2.0])):
    try:
        attempt()
    except QlibUnavailableError as error:
        assert "research" in str(error), error
    else:
        raise AssertionError("A call that needs Qlib succeeded without it.")

print("absent", len(names))
"""
    )

    assert reported.startswith("absent ")


def test_the_adapter_is_a_pqt_model() -> None:
    # Checked by mypy, structurally: the adapter satisfies the protocol
    # without inheriting from PQT or exposing anything of Qlib's.
    model: Model = QlibLightGBM()

    assert isinstance(model, QlibLightGBM)


def _types_in(annotation: object) -> list[type]:
    found = [annotation] if isinstance(annotation, type) else []

    for argument in typing.get_args(annotation):
        found += _types_in(argument)

    return found


def _public_callables(owner: type) -> list[Callable[..., object]]:
    found: list[Callable[..., object]] = []

    for name, member in inspect.getmembers(owner):
        if name.startswith("_") and name != "__init__":
            continue

        if isinstance(member, property) and member.fget is not None:
            found.append(member.fget)
        elif inspect.isfunction(member):
            found.append(member)

    return found


def test_no_engine_type_crosses_the_adapters_public_surface() -> None:
    annotated: list[type] = []

    for name in adapter.__all__:
        exported = getattr(adapter, name)
        callables = _public_callables(exported) if inspect.isclass(exported) else [exported]
        callables = [item for item in callables if callable(item)]

        for item in callables:
            for hint in typing.get_type_hints(item).values():
                annotated += _types_in(hint)

        if inspect.isclass(exported):
            for hint in typing.get_type_hints(exported).values():
                annotated += _types_in(hint)

    leaked = [kind for kind in annotated if kind.__module__.split(".")[0] in ENGINE_PACKAGES]

    assert annotated, "No annotations were inspected; the check proves nothing."
    assert leaked == []


@pytest.mark.parametrize(
    ("features", "labels", "message"),
    [
        ([], [], "empty"),
        ([[]], [1.0], "no features"),
        ([[1.0, 2.0], [3.0]], [1.0, 2.0], "row 1 has 1 features"),
        ([[1.0], [float("nan")]], [1.0, 2.0], "not finite"),
        ([[1.0], [float("inf")]], [1.0, 2.0], "not finite"),
        ([[1.0], [2.0]], [1.0], "given 1 labels"),
        ([[1.0], [2.0]], [1.0, float("nan")], "label is not finite"),
        ([[1.0], ["2.0"]], [1.0, 2.0], "row 1 holds a value that is not a number"),
        ([[1.0], 2.0], [1.0, 2.0], "row 1 is not a sequence"),
        (["ab", "cd"], [1.0, 2.0], "row 0 is text"),
        ([[1.0], [2.0]], 3.0, "labels is not a sequence"),
        ([[1.0], [2.0]], [1.0, None], "label holds a value that is not a number"),
    ],
)
def test_fit_refuses_rows_it_cannot_train_on_before_touching_the_engine(
    features: object, labels: object, message: str
) -> None:
    # Refused whether or not Qlib is installed: the rows are PQT's to check,
    # and always with a ValueError, whatever shape of wrong they are — some
    # of these are not even the types the signature promises.
    with pytest.raises(ValueError, match=message):
        QlibLightGBM().fit(
            cast("Sequence[Sequence[float]]", features), cast("Sequence[float]", labels)
        )


def test_fit_refuses_validation_rows_of_another_width() -> None:
    with pytest.raises(ValueError, match="as wide"):
        QlibLightGBM().fit([[1.0, 2.0]], [1.0], validation=([[1.0]], [1.0]))


def test_predict_refuses_before_fit() -> None:
    with pytest.raises(ValueError, match="not been fitted"):
        QlibLightGBM().predict([[1.0]])


@pytest.mark.parametrize(
    "settings",
    [
        {"num_boost_round": 0},
        {"early_stopping_rounds": 0},
        {"num_leaves": 0},
        {"min_data_in_leaf": 0},
        {"num_threads": 0},
        {"learning_rate": 0.0},
        {"bagging_freq": -1},
        {"bagging_fraction": 0.0},
        {"bagging_fraction": 1.5},
        {"feature_fraction": 0.0},
        {"feature_fraction": 1.01},
    ],
)
def test_settings_refuse_values_that_cannot_train(settings: dict[str, Any]) -> None:
    with pytest.raises(ValueError, match="must be"):
        LightGBMSettings(**settings)


def test_unavailability_names_the_extra() -> None:
    assert "[research]" in str(QlibUnavailableError())


def test_a_run_records_the_engine_by_its_import_name() -> None:
    # Pinned here because this file may name the engine (see the guard).
    assert adapter.ENGINE == "qlib"


def test_the_default_scratch_is_private_to_the_process_and_removed_at_exit() -> None:
    # Made without Qlib: the directory is the adapter's, not the engine's.
    reported = _run(
        """
import os
import tempfile
from pathlib import Path

from personal_quant.integrations.qlib.session import default_scratch

scratch = default_scratch()
assert scratch == default_scratch(), "One scratch per process."
assert scratch.is_dir()
assert scratch.parent == Path(tempfile.gettempdir())
(scratch / "left-behind.txt").write_text("removed at exit", encoding="utf-8")
print(scratch)
print(oct(os.stat(scratch).st_mode))
"""
    ).splitlines()
    scratch, mode = Path(reported[-2]), int(reported[-1], 8)

    assert scratch.name.startswith("pqt-qlib-")
    assert not scratch.exists(), "The scratch outlived its process."

    if os.name == "posix":
        assert stat.S_IMODE(mode) == 0o700


def test_two_processes_do_not_share_a_default_scratch() -> None:
    code = """
from personal_quant.integrations.qlib.session import default_scratch

print(default_scratch())
"""

    assert _run(code).splitlines()[-1] != _run(code).splitlines()[-1]
