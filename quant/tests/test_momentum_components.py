"""The pieces of the momentum vertical slice, each on inputs small enough to check by hand."""

from __future__ import annotations

import math
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.analytics.information import information_coefficient, summarise
from personal_quant.backtesting.returns import forward_returns, weighted_return
from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.history import evaluate
from personal_quant.research.protocols import Lookback
from personal_quant.strategies.portfolio import TopQuantile, quantile_buckets
from personal_quant.strategies.signals import CrossSectionalRank
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 8)


def _dataset(tmp_path: Path, closes: dict[str, list[int | None]]) -> CanonicalDataset:
    bars = [
        SyntheticBar(instrument, SESSIONS[index], Decimal(close))
        for instrument, series in closes.items()
        for index, close in enumerate(series)
        if close is not None
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(bars=bars, spells={name: [(SESSIONS[0], None)] for name in closes}),
    )
    return LocalDatasetProvider(tmp_path).open("0123456789abcdef")


# --- factor -----------------------------------------------------------------


def test_momentum_declares_its_formation_window_plus_the_as_of() -> None:
    # 12-1: from 252 sessions back to 21 back, so 253 closes are read.
    assert Momentum().lookback == Lookback(253)
    assert Momentum(formation=4, skip=1).lookback == Lookback(5)


def test_momentum_is_the_return_over_the_formation_window_skipping_the_latest(
    tmp_path: Path,
) -> None:
    # Formation 4, skip 1, at the fifth session: from the first close to the
    # fourth. The fifth — the most recent — is left out, as 12-1 leaves out the
    # last month.
    dataset = _dataset(tmp_path, {"aaa": [100, 90, 95, 120, 500, 0, 0, 0]})

    values = evaluate(Momentum(formation=4, skip=1), dataset, SESSIONS[4])

    assert values == {"aaa": pytest.approx(0.2)}


def test_momentum_is_absent_without_a_close_at_either_end(tmp_path: Path) -> None:
    dataset = _dataset(
        tmp_path,
        {"aaa": [None, 90, 95, 120, 500, 1, 1, 1], "bbb": [100, 90, 95, None, 500, 1, 1, 1]},
    )

    assert evaluate(Momentum(formation=4, skip=1), dataset, SESSIONS[4]) == {}


def test_momentum_is_absent_before_a_full_window_exists(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path, {"aaa": [100, 90, 95, 120, 500, 1, 1, 1]})

    assert evaluate(Momentum(formation=4, skip=1), dataset, SESSIONS[3]) == {}


@pytest.mark.parametrize(("formation", "skip"), [(0, 0), (5, 5), (5, -1)])
def test_a_window_that_measures_nothing_is_refused(formation: int, skip: int) -> None:
    with pytest.raises(ValueError, match="formation"):
        Momentum(formation=formation, skip=skip)


# --- signal -----------------------------------------------------------------


def test_the_rank_is_a_percentile_with_ties_sharing_the_average() -> None:
    signal = CrossSectionalRank().signal({"a": 0.1, "b": 0.3, "c": 0.3, "d": -0.2})

    assert signal == {"d": 0.25, "a": 0.5, "b": 0.875, "c": 0.875}


def test_an_empty_cross_section_ranks_to_nothing() -> None:
    assert CrossSectionalRank().signal({}) == {}


# --- portfolio --------------------------------------------------------------


def test_buckets_run_from_the_lowest_signal_to_the_highest() -> None:
    signal = {f"i{index}": float(index) for index in range(10)}

    buckets = quantile_buckets(signal, 5)

    assert buckets == [
        frozenset({"i0", "i1"}),
        frozenset({"i2", "i3"}),
        frozenset({"i4", "i5"}),
        frozenset({"i6", "i7"}),
        frozenset({"i8", "i9"}),
    ]


def test_an_uneven_cross_section_still_places_every_instrument_once() -> None:
    signal = {f"i{index}": float(index) for index in range(7)}

    buckets = quantile_buckets(signal, 3)

    assert sorted(len(bucket) for bucket in buckets) == [2, 2, 3]
    assert frozenset().union(*buckets) == set(signal)


def test_the_top_quantile_is_held_in_equal_weights() -> None:
    signal = {f"i{index}": float(index) for index in range(10)}

    assert TopQuantile(quantiles=5).weights(signal) == {"i8": 0.5, "i9": 0.5}


def test_fewer_instruments_than_quantiles_is_refused() -> None:
    with pytest.raises(ValueError, match="quantiles"):
        quantile_buckets({"a": 1.0, "b": 2.0}, 3)


# --- returns ----------------------------------------------------------------


def test_a_forward_return_runs_from_one_close_to_a_later_one(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path, {"aaa": [100, 1, 1, 110, 1, 1, 1, 1]})

    returns = forward_returns(dataset, SESSIONS[0], SESSIONS[3], {"aaa"})

    assert returns == {"aaa": pytest.approx(0.1)}


def test_an_instrument_whose_bars_stop_exits_at_its_last_close(tmp_path: Path) -> None:
    # A name that leaves a point-in-time universe has no bars after its spell;
    # it is taken out at the last close it had, not dropped from the period.
    dataset = _dataset(tmp_path, {"aaa": [100, 105, None, None, None, None, None, None]})

    returns = forward_returns(dataset, SESSIONS[0], SESSIONS[3], {"aaa"})

    assert returns == {"aaa": pytest.approx(0.05)}


def test_an_instrument_that_leaves_the_next_session_is_held_flat_not_dropped(
    tmp_path: Path,
) -> None:
    # Its last close is the entry close. Dropping it would remove exactly the
    # names that left the universe from the portfolios that held them.
    dataset = _dataset(tmp_path, {"aaa": [100, None, None, None, None, None, None, None]})

    assert forward_returns(dataset, SESSIONS[0], SESSIONS[3], {"aaa"}) == {"aaa": 0.0}


def test_an_instrument_with_no_entry_close_has_no_return(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path, {"aaa": [None, 100, 1, 110, 1, 1, 1, 1]})

    assert forward_returns(dataset, SESSIONS[0], SESSIONS[3], {"aaa"}) == {}


def test_a_portfolio_return_reweights_over_the_names_that_have_one() -> None:
    weights = {"a": 0.5, "b": 0.25, "c": 0.25}

    assert weighted_return(weights, {"a": 0.1, "b": -0.1}) == pytest.approx(0.1 / 3)
    assert weighted_return(weights, {}) is None


# --- information coefficient ------------------------------------------------


def test_a_perfect_ordering_has_an_ic_of_one() -> None:
    scores = {"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0}

    assert information_coefficient(
        scores, {"a": -0.1, "b": 0.0, "c": 0.2, "d": 0.5}
    ) == pytest.approx(1.0)
    assert information_coefficient(
        scores, {"a": 0.5, "b": 0.2, "c": 0.0, "d": -0.1}
    ) == pytest.approx(-1.0)


def test_an_ic_needs_three_names_and_some_spread() -> None:
    assert information_coefficient({"a": 1.0, "b": 2.0}, {"a": 0.1, "b": 0.2}) is None
    assert (
        information_coefficient({"a": 1.0, "b": 2.0, "c": 3.0}, {"a": 0.1, "b": 0.1, "c": 0.1})
        is None
    )


def test_the_ic_summary_is_mean_over_deviation() -> None:
    summary = summarise([0.1, 0.3, 0.2])

    assert summary.periods == 3
    assert summary.mean == pytest.approx(0.2)
    assert summary.deviation == pytest.approx(0.1)
    assert summary.information_ratio == pytest.approx(2.0)
    assert summary.t_statistic == pytest.approx(2.0 * math.sqrt(3))


def test_one_period_has_no_deviation_to_divide_by() -> None:
    summary = summarise([0.1])

    assert summary.mean == pytest.approx(0.1)
    assert summary.information_ratio is None
