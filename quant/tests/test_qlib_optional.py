"""Qlib is optional: PQT runs without it, and the adapter says so plainly (ADR-017).

These run in every environment, with or without the ``research`` extra. The
adapter's behaviour with Qlib present is in ``test_qlib_adapter.py``, which
skips without it.

Absence is not taken on trust from the environment the suite happens to run
in. A fresh interpreter has Qlib made unimportable — ``sys.modules["qlib"]``
set to ``None``, which is how Python itself marks a module as unavailable — and
every module in the package is imported there; a second interpreter imports
them all with Qlib importable and checks that none of them loaded it.
"""

from __future__ import annotations

import inspect
import os
import subprocess
import sys
import textwrap
import typing
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from personal_quant.integrations import qlib as adapter
from personal_quant.integrations.qlib import LightGBMSettings, QlibLightGBM, QlibUnavailableError
from personal_quant.research.protocols import Model

SOURCE = Path(__file__).resolve().parents[1] / "src"
ENGINE_PACKAGES = frozenset({"qlib", "mlflow", "lightgbm", "pandas", "numpy"})

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


def test_no_module_of_the_package_imports_qlib_on_import() -> None:
    loaded = _run(
        _IMPORT_EVERYTHING
        + """
print(len(names))
print(",".join(sorted(m for m in sys.modules if m.split(".")[0] in {"qlib", "mlflow", "lightgbm"})))
"""
    )
    count, engines = loaded.splitlines() if "\n" in loaded else (loaded, "")

    assert int(count) > 20, "The walk imported too few modules to prove anything."
    assert "personal_quant" not in engines
    assert engines == ""


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
    ],
)
def test_fit_refuses_rows_it_cannot_train_on_before_touching_the_engine(
    features: list[list[float]], labels: list[float], message: str
) -> None:
    # Refused whether or not Qlib is installed: the rows are PQT's to check.
    with pytest.raises(ValueError, match=message):
        QlibLightGBM().fit(features, labels)


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
    ],
)
def test_settings_refuse_values_that_cannot_train(settings: dict[str, Any]) -> None:
    with pytest.raises(ValueError, match="must be"):
        LightGBMSettings(**settings)


def test_unavailability_names_the_extra() -> None:
    assert "[research]" in str(QlibUnavailableError())
