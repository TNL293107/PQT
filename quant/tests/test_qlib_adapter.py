"""Qlib's LightGBM through PQT's ``Model`` protocol, with the real engine (U8).

Skipped without the ``research`` extra, which needs Python 3.12 (see
``quant/README.md``); CI's default job does not install it (ADR-017). What
holds without Qlib — the guard, optionality, refusals — is in
``test_qlib_confinement.py`` and ``test_qlib_optional.py``.

The rows are synthetic and built in the test, as PQT would build them before
calling a model: Qlib's data layer is never involved.

Two claims cannot be shown inside this process, because Qlib's configuration
is process-wide and the first fit here fixes it: where the recorder writes
when the process runs in a git checkout, and whether a fit is repeatable in a
different process. Those fit in a fresh interpreter.
"""

from __future__ import annotations

import json
import os
import random
import shutil
import statistics
import subprocess
import sys
import threading
import uuid
from collections.abc import Mapping
from dataclasses import asdict, replace
from importlib.metadata import distribution, version
from pathlib import Path
from typing import Any

import pytest

from personal_quant.integrations.qlib import (
    ENGINE,
    LightGBMSettings,
    QlibLightGBM,
    available,
    engine_version,
)

pytestmark = [
    pytest.mark.skipif(
        not available(),
        reason='Qlib is not installed; it is optional (ADR-017). pip install -e ".[research]" '
        "on Python 3.12 to run the adapter against the real engine.",
    ),
    # MLflow, which Qlib's recorder writes to inside the adapter, uses a
    # SQLAlchemy loader that SQLAlchemy 2.1 deprecates. It is MLflow's to fix
    # and changes nothing the adapter returns; every other warning still fails.
    pytest.mark.filterwarnings("ignore:The ``noload`` loader strategy is deprecated"),
]

SOURCE = Path(__file__).resolve().parents[1] / "src"
SETTINGS = LightGBMSettings(num_boost_round=60, min_data_in_leaf=5, seed=7)

# Settings under which the seed decides the trees: each round bags half the
# rows and each tree sees two of the three features, both drawn from the seed.
SAMPLED = LightGBMSettings(
    num_boost_round=40,
    min_data_in_leaf=5,
    bagging_fraction=0.5,
    bagging_freq=1,
    feature_fraction=0.67,
    seed=7,
)

# A fit in a fresh interpreter: settings and rows arrive on stdin as JSON, the
# scratch as an argument (or the process's default without one), and the
# predictions and the scratch used leave as the last line of stdout.
_FIT_IN_A_FRESH_PROCESS = """
import json
import sys
from pathlib import Path

from personal_quant.integrations.qlib import LightGBMSettings, QlibLightGBM
from personal_quant.integrations.qlib.session import default_scratch

given = json.load(sys.stdin)
scratch = Path(sys.argv[1]) if len(sys.argv) > 1 else default_scratch()
model = QlibLightGBM(LightGBMSettings(**given["settings"]), scratch=scratch)
predicted = model.fit(given["features"], given["labels"]).predict(given["unseen"])
print(json.dumps({"predicted": list(predicted), "scratch": str(scratch)}))
"""


@pytest.fixture(scope="module")
def scratch(tmp_path_factory: pytest.TempPathFactory) -> Path:
    # One scratch for the module: Qlib's first initialisation in a process,
    # with MLflow creating its database, takes most of a minute; later fits
    # take about a second.
    return tmp_path_factory.mktemp("qlib-scratch")


def _rows(count: int, seed: int) -> tuple[list[list[float]], list[float]]:
    # Two features carry the label; the third is noise the model should ignore.
    rng = random.Random(seed)  # noqa: S311 - seeded test rows, not a secret.
    features = [[rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)] for _ in range(count)]
    labels = [2.0 * row[0] - row[1] + rng.gauss(0, 0.1) for row in features]

    return features, labels


def _fit_elsewhere(
    settings: LightGBMSettings,
    rows: tuple[list[list[float]], list[float]],
    unseen: list[list[float]],
    *,
    cwd: Path,
    scratch: Path | None = None,
) -> Mapping[str, Any]:
    given = {
        "settings": asdict(settings),
        "features": rows[0],
        "labels": rows[1],
        "unseen": unseen,
    }
    environment = {**os.environ, "PYTHONPATH": str(SOURCE), "MLFLOW_DISABLE_AGENT_HINT": "1"}
    completed = subprocess.run(  # noqa: S603 - the interpreter running these tests.
        [sys.executable, "-c", _FIT_IN_A_FRESH_PROCESS, *([str(scratch)] if scratch else [])],
        input=json.dumps(given),
        capture_output=True,
        text=True,
        cwd=cwd,
        env=environment,
        check=False,
        timeout=300,
    )

    assert completed.returncode == 0, completed.stderr

    result: Mapping[str, Any] = json.loads(completed.stdout.strip().rpartition("\n")[2])

    return result


def test_the_engine_version_is_the_pinned_release() -> None:
    assert engine_version() == version("pyqlib") == "0.9.7"


def test_the_engine_is_named_for_the_pinned_distribution() -> None:
    files = distribution("pyqlib").files or []

    assert any(file.parts[0] == ENGINE for file in files)


def test_it_learns_a_relationship_from_pqt_rows(scratch: Path) -> None:
    features, labels = _rows(400, seed=1)
    unseen, truth = _rows(100, seed=2)

    model = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels)
    predicted = model.predict(unseen)

    assert len(predicted) == len(unseen)
    assert statistics.correlation(predicted, truth) > 0.9


def test_what_comes_back_is_builtin(scratch: Path) -> None:
    features, labels = _rows(200, seed=3)
    model = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels)

    predicted = model.predict(features)

    assert type(predicted) is tuple
    assert all(type(value) is float for value in predicted)
    assert all(type(value) is float for values in model.training_curve.values() for value in values)


def test_the_seed_decides_a_sampled_fit(scratch: Path) -> None:
    # Without this, agreement between two fits below would prove nothing: with
    # no sampling LightGBM never consults the seed.
    features, labels = _rows(300, seed=4)
    unseen, _ = _rows(50, seed=5)
    reseeded = replace(SAMPLED, seed=8)

    first = QlibLightGBM(SAMPLED, scratch=scratch).fit(features, labels).predict(unseen)
    other = QlibLightGBM(reseeded, scratch=scratch).fit(features, labels).predict(unseen)

    assert first != other


def test_the_same_seed_gives_the_same_sampled_fit(scratch: Path) -> None:
    features, labels = _rows(300, seed=4)
    unseen, _ = _rows(50, seed=5)

    first = QlibLightGBM(SAMPLED, scratch=scratch).fit(features, labels).predict(unseen)
    second = QlibLightGBM(SAMPLED, scratch=scratch).fit(features, labels).predict(unseen)

    assert first == second


def test_the_same_seed_gives_the_same_sampled_fit_in_another_process(
    scratch: Path, tmp_path: Path
) -> None:
    rows = _rows(300, seed=4)
    unseen, _ = _rows(50, seed=5)

    here = QlibLightGBM(SAMPLED, scratch=scratch).fit(*rows).predict(unseen)
    elsewhere = _fit_elsewhere(SAMPLED, rows, unseen, cwd=tmp_path)

    # Bit for bit: JSON carries a float's shortest repr, which round-trips.
    assert tuple(elsewhere["predicted"]) == here
    # And that process's default scratch went with it.
    assert Path(elsewhere["scratch"]).name.startswith("pqt-qlib-")
    assert not Path(elsewhere["scratch"]).exists()


def test_fits_in_two_threads_take_turns_and_agree(scratch: Path) -> None:
    features, labels = _rows(200, seed=12)
    unseen, _ = _rows(20, seed=13)
    results: list[tuple[float, ...]] = []
    failures: list[BaseException] = []

    def fit() -> None:
        try:
            model = QlibLightGBM(SAMPLED, scratch=scratch).fit(features, labels)
            results.append(model.predict(unseen))
        except BaseException as error:  # reported by the assertions below
            failures.append(error)

    threads = [threading.Thread(target=fit) for _ in range(2)]

    for thread in threads:
        thread.start()

    for thread in threads:
        thread.join(timeout=300)

    assert failures == []
    assert len(results) == 2
    assert results[0] == results[1]


def test_without_validation_it_boosts_every_round(scratch: Path) -> None:
    features, labels = _rows(200, seed=6)

    model = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels)

    assert list(model.training_curve) == ["l2.train"]
    assert len(model.training_curve["l2.train"]) == SETTINGS.num_boost_round


def test_validation_rows_are_used_for_early_stopping(scratch: Path) -> None:
    features, labels = _rows(300, seed=8)
    held_out = _rows(100, seed=9)
    settings = LightGBMSettings(
        num_boost_round=2_000, early_stopping_rounds=5, learning_rate=0.3, min_data_in_leaf=5
    )

    model = QlibLightGBM(settings, scratch=scratch).fit(features, labels, validation=held_out)

    assert set(model.training_curve) == {"l2.train", "l2.valid"}
    assert len(model.training_curve["l2.valid"]) < settings.num_boost_round


def test_predict_refuses_rows_of_another_width(scratch: Path) -> None:
    features, labels = _rows(100, seed=10)
    model = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels)

    with pytest.raises(ValueError, match="fitted on 3 features"):
        model.predict([[1.0, 2.0]])


def test_describe_records_the_engine_and_its_environment_for_a_run(scratch: Path) -> None:
    described = QlibLightGBM(SETTINGS, scratch=scratch).describe()
    # Keyed "<distribution>_version", so this file need not name MLflow,
    # which the guard keeps inside the adapter.
    libraries = {
        key: value
        for key, value in described.items()
        if key.endswith("_version") and key != "research_engine_version"
    }

    assert described["research_engine"] == ENGINE
    assert described["research_engine_version"] == "0.9.7"
    assert set(libraries) == {
        "lightgbm_version",
        "numpy_version",
        "pandas_version",
        "mlflow_version",
    }
    assert all(value == version(key.removesuffix("_version")) for key, value in libraries.items())
    assert described["settings"] == {
        "num_boost_round": 60,
        "early_stopping_rounds": 20,
        "learning_rate": 0.05,
        "num_leaves": 31,
        "min_data_in_leaf": 5,
        "bagging_fraction": 1.0,
        "bagging_freq": 0,
        "feature_fraction": 1.0,
        "seed": 7,
        "num_threads": 1,
    }


def _git(working: Path, *arguments: str) -> str:
    git = shutil.which("git")
    assert git is not None

    return subprocess.run(  # noqa: S603 - git, on a directory this test made.
        [git, "-c", "user.name=PQT", "-c", "user.email=pqt@example.invalid", *arguments],
        cwd=working,
        capture_output=True,
        text=True,
        check=True,
    ).stdout


def _tree(root: Path) -> list[str]:
    return sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if ".git" not in path.relative_to(root).parts
    )


@pytest.mark.skipif(shutil.which("git") is None, reason="git is needed to make a checkout.")
def test_qlib_writes_only_into_the_scratch_from_a_git_checkout(tmp_path: Path) -> None:
    # Qlib's recorder saves `git diff` and `git status` of the working
    # directory with every run; in a checkout with an uncommitted change it
    # has something to write. In a fresh process, so Qlib is initialised
    # here, from this directory, with this scratch.
    working = tmp_path / "checkout"
    scratch = tmp_path / "scratch"
    working.mkdir()
    marker = f"uncommitted-{uuid.uuid4().hex}"
    _git(working, "init", "--quiet")
    (working / "strategy.txt").write_text("committed\n", encoding="utf-8")
    _git(working, "add", "strategy.txt")
    _git(working, "commit", "--quiet", "-m", "A commit to differ from.")
    (working / "strategy.txt").write_text(f"committed\n{marker}\n", encoding="utf-8")
    before, status = _tree(working), _git(working, "status", "--porcelain")

    _fit_elsewhere(SETTINGS, _rows(100, seed=11), [[0.0, 0.0, 0.0]], cwd=working, scratch=scratch)

    diffs = list((scratch / "artifacts").rglob("code_diff.txt"))

    assert _tree(working) == before == ["strategy.txt"]
    assert _git(working, "status", "--porcelain") == status
    assert diffs, "The recorder saved no diff, so this proves nowhere it would have gone."
    assert any(marker in diff.read_text(encoding="utf-8") for diff in diffs)
    assert list(scratch.glob("*.db"))
    assert not any((scratch / "unused-provider").iterdir())
