"""Qlib's LightGBM through PQT's ``Model`` protocol, with the real engine (U8).

Skipped without the ``research`` extra, which needs Python 3.12 (see
``quant/README.md``); CI's default job does not install it (ADR-017). What
holds without Qlib — the guard, optionality, refusals — is in
``test_qlib_confinement.py`` and ``test_qlib_optional.py``.

The rows are synthetic and built in the test, as PQT would build them before
calling a model: Qlib's data layer is never involved.
"""

from __future__ import annotations

import random
import statistics
from importlib.metadata import version
from pathlib import Path

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

SETTINGS = LightGBMSettings(num_boost_round=60, min_data_in_leaf=5, seed=7)


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


def test_the_engine_version_is_the_pinned_release() -> None:
    assert engine_version() == version("pyqlib") == "0.9.7"


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


def test_the_same_settings_and_rows_give_the_same_predictions(scratch: Path) -> None:
    features, labels = _rows(300, seed=4)
    unseen, _ = _rows(50, seed=5)

    first = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels).predict(unseen)
    second = QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels).predict(unseen)

    assert first == second


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


def test_describe_records_the_engine_for_a_run(scratch: Path) -> None:
    described = QlibLightGBM(SETTINGS, scratch=scratch).describe()

    assert described["research_engine"] == ENGINE == "qlib"
    assert described["research_engine_version"] == "0.9.7"
    assert described["lightgbm_version"] == version("lightgbm")
    assert described["settings"] == {
        "num_boost_round": 60,
        "early_stopping_rounds": 20,
        "learning_rate": 0.05,
        "num_leaves": 31,
        "min_data_in_leaf": 5,
        "seed": 7,
        "num_threads": 1,
    }


def test_qlib_writes_only_into_the_scratch(
    scratch: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    working = tmp_path / "working"
    working.mkdir()
    monkeypatch.chdir(working)
    features, labels = _rows(100, seed=11)

    QlibLightGBM(SETTINGS, scratch=scratch).fit(features, labels)

    assert list(working.iterdir()) == []
    assert (scratch / "mlflow.db").is_file()
    assert not any((scratch / "unused-provider").iterdir())
