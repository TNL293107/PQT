"""Sprint 11.1 acceptance (ADR-037), written by QA + QC before the code.

Red until 11.1 lands; the code is done when these pass unedited. ADR-037's
Consequences name ``calibrate``, ``expected_value`` and ``CostModel`` in
``personal_quant.analytics.calibration``. These tests also pin two names the
ADR describes but does not name, in the same module:

* ``known_at(rebalances, t)``: the structural guard of ADR-037 §4, the
  rebalances whose **exit** is at or before ``t`` and nothing else;
* ``p_model(labels, minimum=20)``: the running frequency of ``label > 0``, or
  ``None`` below ``minimum`` observations.

``calibrate(predictions, outcomes)`` returns ``brier``, ``gap`` (count-weighted
mean absolute reliability gap), ``count`` and ``bins``: always three, at the
predictions' own terciles, an empty one with ``count`` 0 and ``predicted`` and
``realised`` ``None``. **Equal predictions share a bin**, which is what makes
a constant model have a gap of 0. The quantile method is left to the code; the
cases below give the same bins under any of them.

Every expected value is written out by hand below, not computed by the code
under test. Imports are inside each test so one missing name fails one test.
"""

from __future__ import annotations

from datetime import date
from pathlib import Path

import pytest

from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.labelled import LabelledDataset
from test_evaluation_acceptance import DATASET, SESSIONS, SOUND, _write

# --- ADR-037 §2: calibration -------------------------------------------------


def test_a_constant_half_on_a_balanced_outcome_has_a_brier_of_a_quarter() -> None:
    from personal_quant.analytics.calibration import calibrate

    result = calibrate([0.5] * 8, [1, 0, 1, 1, 0, 0, 1, 0])

    assert result.brier == pytest.approx(0.25, abs=1e-12)
    assert result.count == 8


def test_a_constant_base_rate_model_has_brier_equal_to_the_variance_and_no_gap() -> None:
    from personal_quant.analytics.calibration import calibrate

    # Base rate 3/4, so the variance is 3/4 x 1/4 = 0.1875. Every prediction is
    # equal, so all four share one bin and the other two are empty.
    result = calibrate([0.75] * 4, [1, 1, 1, 0])

    assert result.brier == pytest.approx(0.1875, abs=1e-12)
    assert result.gap == pytest.approx(0.0, abs=1e-12)
    assert len(result.bins) == 3
    assert sorted(item.count for item in result.bins) == [0, 0, 4]
    assert all(item.realised is None for item in result.bins if item.count == 0)


def test_a_model_equal_to_each_bin_s_realised_frequency_has_no_gap_and_a_lower_brier() -> None:
    from personal_quant.analytics.calibration import calibrate

    # Ten observations at each of 0.2, 0.5 and 0.8, realised 2, 5 and 8 times:
    # each tercile bin's mean prediction equals its realised frequency. Brier is
    # the mean of p(1 - p) per bin, (0.16 + 0.25 + 0.16) / 3 = 0.19; the base
    # rate is 15/30, so the variance is 0.25.
    predictions = [0.2] * 10 + [0.5] * 10 + [0.8] * 10
    outcomes = [1] * 2 + [0] * 8 + [1] * 5 + [0] * 5 + [1] * 8 + [0] * 2

    result = calibrate(predictions, outcomes)

    assert result.gap == pytest.approx(0.0, abs=1e-12)
    assert result.brier == pytest.approx(0.19, abs=1e-12)
    assert result.brier <= 0.25
    assert [(item.count, item.realised) for item in result.bins] == [
        (10, pytest.approx(0.2)),
        (10, pytest.approx(0.5)),
        (10, pytest.approx(0.8)),
    ]


def test_the_gap_is_weighted_by_each_bin_s_count() -> None:
    from personal_quant.analytics.calibration import calibrate

    # Bins of 10 at 0.2 (realised 0.4) and 20 at 0.8 (realised 0.8):
    # (10 x 0.2 + 20 x 0.0) / 30 = 0.0667, not the unweighted (0.2 + 0) / 2.
    predictions = [0.2] * 10 + [0.8] * 20
    outcomes = [1] * 4 + [0] * 6 + [1] * 16 + [0] * 4

    result = calibrate(predictions, outcomes)

    assert result.gap == pytest.approx(2.0 / 30.0, abs=1e-12)


# --- ADR-037 §1 and §4: the running estimate and its guard -------------------


def test_p_model_is_the_frequency_of_a_positive_label_and_absent_below_twenty() -> None:
    from personal_quant.analytics.calibration import p_model

    labels = [0.01] * 13 + [-0.02] * 6 + [0.0]  # 20 labels, 13 positive; 0.0 is not > 0

    assert p_model(labels) == pytest.approx(13 / 20, abs=1e-12)
    assert p_model(labels[:19]) is None


def test_only_rebalances_whose_exit_is_at_or_before_t_are_known_at_t(
    tmp_path: Path,
) -> None:
    from personal_quant.analytics.calibration import known_at

    _write(tmp_path)
    rebalances = list(
        LabelledDataset.open(LocalDatasetProvider(tmp_path), DATASET, SOUND).rebalances()
    )
    # Rebalances at sessions 3, 6 and 9, exits at 6, 9 and 12 (horizon 3).
    assert [item.session for item in rebalances] == [
        SESSIONS[3],
        SESSIONS[6],
        SESSIONS[9],
    ]

    at_6 = known_at(rebalances, SESSIONS[6])
    at_3 = known_at(rebalances, SESSIONS[3])

    # The rebalance at 6 itself is excluded: its label ends at 9, after t. An
    # estimator that also read t's own label would include it.
    assert [item.session for item in at_6] == [SESSIONS[3]]
    assert at_3 == []
    assert all(item.exit <= SESSIONS[6] for item in at_6)


def test_known_at_reads_the_exit_not_the_session() -> None:
    from personal_quant.analytics.calibration import known_at

    class _Rebalance:
        def __init__(self, session: date, exit_session: date) -> None:
            self.session = session
            self.exit = exit_session

    # A horizon longer than the rebalance interval: the rebalance at day 1
    # exits at day 10, so at day 5 its label is not known yet, although its
    # session is before t. "session < t" would leak it.
    early = _Rebalance(date(2026, 1, 1), date(2026, 1, 10))
    done = _Rebalance(date(2025, 12, 1), date(2025, 12, 20))

    assert known_at([done, early], date(2026, 1, 5)) == [done]


# --- ADR-037 §1 and §3: cost and expected value ------------------------------


def test_the_v1_round_trip_is_the_sum_of_the_five_figures_with_slippage_twice() -> None:
    from personal_quant.analytics.calibration import CostModel

    # 0.0015 + 0.0015 + 0.001 + 0.001 + 2 x 0 = 0.005
    cost = CostModel(buy_fee=0.0015, sell_fee=0.0015, sell_tax=0.001, spread=0.001, slippage=0.0)
    with_slippage = CostModel(
        buy_fee=0.0015, sell_fee=0.0015, sell_tax=0.001, spread=0.001, slippage=0.0005
    )

    assert cost.round_trip == pytest.approx(0.005, abs=1e-15)
    assert with_slippage.round_trip == pytest.approx(0.006, abs=1e-15)


def test_expected_value_is_by_hand_and_loss_is_a_magnitude() -> None:
    from personal_quant.analytics.calibration import CostModel, expected_value

    cost = CostModel(buy_fee=0.0015, sell_fee=0.0015, sell_tax=0.001, spread=0.001, slippage=0.0)

    # 0.6 x 0.05 - 0.4 x 0.04 - 0.005 = 0.030 - 0.016 - 0.005 = 0.009
    assert expected_value(0.6, 0.05, 0.04, cost) == pytest.approx(0.009, abs=1e-12)


@pytest.mark.parametrize("missing", ["p", "gain", "loss", "cost"])
def test_expected_value_is_absent_when_any_input_is_absent_never_zero(
    missing: str,
) -> None:
    from personal_quant.analytics.calibration import CostModel, expected_value

    cost = CostModel(buy_fee=0.0015, sell_fee=0.0015, sell_tax=None, spread=0.001, slippage=0.0)
    full = CostModel(buy_fee=0.0015, sell_fee=0.0015, sell_tax=0.001, spread=0.001, slippage=0.0)
    inputs = {"p": 0.6, "gain": 0.05, "loss": 0.04, "cost": full}
    inputs[missing] = cost if missing == "cost" else None

    assert expected_value(inputs["p"], inputs["gain"], inputs["loss"], inputs["cost"]) is None
