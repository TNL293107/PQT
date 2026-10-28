"""Sprint 10 acceptance (ADR-035, ADR-036), written by QA + QC before the code.

Red until 10.1 and 10.2 land; the code is done when these pass unedited. The
contract they pin, beyond the ADRs' own words:

* ``personal_quant.factors.transforms.winsorise_zscore(cross_section)``;
* ``personal_quant.worker.handlers.factor_evaluation_handler(dataset_root,
  engines=None)``, ``engines`` defaulting to the catalogue as
  ``LabelledDataset.open`` does, so a test can hand in its own;
* run metrics ``rank_ic_mean``, ``ic_mean``, ``decay_rank_ic_mean_h{h}`` and
  ``decay_periods_h{h}``.

A name changes only by agreement with QA + QC, so these stay the oracle.
Imports are inside each test so one missing piece fails one test, not all.
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from personal_quant.factors.momentum import Momentum
from personal_quant.research.protocols import FeatureEngine, History, Lookback
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 15)
DATASET = "fedcba9876543210"
HORIZON = 3

# Five names, each compounding at its own constant rate every session, so the
# return over the last HORIZON sessions equals the return over the next
# HORIZON: a trailing-return feature is the label itself, without look-ahead.
RATES = {"aaa": 0.00, "bbb": 0.01, "ccc": 0.02, "ddd": 0.03, "eee": 0.04}

TRAILING = Momentum(formation=HORIZON, skip=0)  # momentum_3_0, lookback 4


class _Negated:
    """The trailing return with its sign flipped: a rank IC of exactly -1."""

    name = "negated_momentum_3_0"
    version = "1"
    lookback = TRAILING.lookback

    def compute(self, history: History) -> Mapping[str, float]:
        return {name: -value for name, value in TRAILING.compute(history).items()}


class _PeeksAtTheLabel:
    """The canary: reads the close HORIZON sessions after the as-of."""

    name = "peeks_at_the_label"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        exit_session = SESSIONS[SESSIONS.index(history.as_of) + HORIZON]
        bar = history.bar("aaa", exit_session)
        return {"aaa": float(bar.close)} if bar is not None else {}


ENGINES: list[FeatureEngine] = [TRAILING, _Negated(), _PeeksAtTheLabel()]
# The canary gets a build of its own: a labelled dataset evaluates every
# feature its request names at each rebalance, so it would fail the others.
SOUND: list[FeatureEngine] = [TRAILING, _Negated()]


def _write(tmp_path: Path, engines: list[FeatureEngine] = SOUND) -> None:
    bars = [
        SyntheticBar(name, session, Decimal(repr(100 * (1 + rate) ** index)))
        for name, rate in RATES.items()
        for index, session in enumerate(SESSIONS)
    ]
    # Rebalances at sessions 3, 6, 9; 12 + 3 passes the fifteenth and is dropped.
    request = {
        "universe": "TEST30",
        "as_of": None,
        "from": SESSIONS[TRAILING.lookback.sessions - 1].isoformat(),
        "to": SESSIONS[-1].isoformat(),
        "interval": "1d",
        "fundamentals": False,
        "rebalance_every": HORIZON,
        "features": [
            {"name": engine.name, "version": 1, "lookback": engine.lookback.sessions}
            for engine in engines
        ],
        "label": {"horizon": HORIZON, "basis": "adjusted"},
    }
    write_dataset(
        tmp_path,
        SyntheticDataset(
            dataset_id=DATASET,
            bars=bars,
            spells={name: [(SESSIONS[0], None)] for name in RATES},
            request=request,
        ),
    )


def _evaluate(tmp_path: Path, feature: str) -> Mapping[str, float | None]:
    from personal_quant.worker.handlers import factor_evaluation_handler

    payload: dict[str, Any] = {
        "experiment": "acceptance",
        "dataset_id": DATASET,
        "dataset_version": 1,
        "feature": feature,
        "transform_version": "raw",
        "quantiles": 5,
        "decay_horizons": [HORIZON, 2 * HORIZON],
    }
    outcome = factor_evaluation_handler(tmp_path, engines=ENGINES)(payload)

    assert outcome.run is not None
    return outcome.run.metrics


# --- ADR-035 §1: wz-1 by hand ------------------------------------------------


def test_wz_1_on_five_names_matches_the_hand_computation() -> None:
    from personal_quant.factors.transforms import winsorise_zscore

    # median 3; |x - 3| = 2, 1, 0, 1, 97, so MAD = 1 and the limit is
    # 3 + 3 x 1.4826 = 7.4478: e is clipped to it, the rest are inside.
    # Mean of (1, 2, 3, 4, 7.4478) = 3.48956; sample deviation 2.47914198...
    clipped = [1.0, 2.0, 3.0, 4.0, 3 + 3 * 1.4826]
    mean = sum(clipped) / 5
    deviation = (sum((value - mean) ** 2 for value in clipped) / 4) ** 0.5

    result = winsorise_zscore({"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0, "e": 100.0})

    assert result.keys() == {"a", "b", "c", "d", "e"}
    for name, value in zip("abcde", clipped, strict=True):
        assert result[name] == pytest.approx((value - mean) / deviation, abs=1e-12)
    assert result["e"] == pytest.approx(1.5966169055220263, abs=1e-12)


# --- ADR-036 §2: a planted factor ---------------------------------------------


def test_a_factor_equal_to_the_label_has_an_ic_of_one(tmp_path: Path) -> None:
    _write(tmp_path)

    metrics = _evaluate(tmp_path, TRAILING.name)

    assert metrics["rank_ic_mean"] == pytest.approx(1.0, abs=1e-12)
    assert metrics["ic_mean"] == pytest.approx(1.0, abs=1e-9)


def test_its_negation_has_an_ic_of_minus_one(tmp_path: Path) -> None:
    _write(tmp_path)

    metrics = _evaluate(tmp_path, _Negated.name)

    assert metrics["rank_ic_mean"] == pytest.approx(-1.0, abs=1e-12)
    assert metrics["ic_mean"] == pytest.approx(-1.0, abs=1e-9)


# --- ADR-036 §6: the look-ahead canary -----------------------------------------


def test_a_feature_reading_the_label_close_is_refused_through_the_job(tmp_path: Path) -> None:
    from personal_quant.research.history import LookAheadError

    _write(tmp_path, [_PeeksAtTheLabel()])

    with pytest.raises(LookAheadError, match=_PeeksAtTheLabel.name):
        _evaluate(tmp_path, _PeeksAtTheLabel.name)


# --- ADR-036 §3: decay --------------------------------------------------------


def test_decay_at_the_rebalance_horizon_equals_the_main_rank_ic(tmp_path: Path) -> None:
    _write(tmp_path)

    metrics = _evaluate(tmp_path, TRAILING.name)

    assert metrics[f"decay_rank_ic_mean_h{HORIZON}"] == metrics["rank_ic_mean"]
    assert metrics[f"decay_periods_h{HORIZON}"] == 3


def test_decay_drops_a_rebalance_whose_horizon_passes_the_last_session(tmp_path: Path) -> None:
    _write(tmp_path)

    metrics = _evaluate(tmp_path, TRAILING.name)

    # Sessions 3 and 6 reach 9 and 12; 9 + 6 = 15 is past the last index.
    assert metrics[f"decay_periods_h{2 * HORIZON}"] == 2
