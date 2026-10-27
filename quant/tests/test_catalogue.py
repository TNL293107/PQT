"""Catalogue v1 (ADR-033 §4): each feature caught over-reaching, checked by hand, absent."""

from __future__ import annotations

import math
from collections.abc import Mapping, Sequence
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.factors.catalogue import CATALOGUE, entry
from personal_quant.factors.liquidity import Liquidity
from personal_quant.factors.momentum import Momentum
from personal_quant.factors.risk import Volatility
from personal_quant.factors.size import Size
from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.history import LookAheadError, LookbackExceededError, evaluate
from personal_quant.research.protocols import FeatureEngine, History, Lookback
from synthetic import (
    SyntheticBar,
    SyntheticDataset,
    SyntheticFundamentals,
    weekdays,
    write_dataset,
)

SESSIONS = weekdays(date(2025, 1, 6), 130)


def _dataset(
    tmp_path: Path,
    bars: Sequence[SyntheticBar],
    fundamentals: list[SyntheticFundamentals] | None = None,
    not_collected: dict[str, str] | None = None,
) -> CanonicalDataset:
    names = sorted({bar.instrument_id for bar in bars})
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=list(bars),
            spells={name: [(SESSIONS[0], None)] for name in names},
            fundamentals=fundamentals,
            not_collected=not_collected or {},
            ratio_version=2,
        ),
    )
    return LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def _series(name: str, closes: Sequence[int | None], turnover: bool = False) -> list[SyntheticBar]:
    return [
        SyntheticBar(
            name,
            SESSIONS[index],
            Decimal(close),
            turnover=Decimal(close) * 1_000 if turnover else None,
        )
        for index, close in enumerate(closes)
        if close is not None
    ]


class _Understated:
    """A twin declaring one session less than the real feature reads."""

    def __init__(self, engine: FeatureEngine) -> None:
        self._engine = engine
        self.name = f"understated_{engine.name}"
        self.version = engine.version
        self.lookback = Lookback(engine.lookback.sessions - 1)

    def compute(self, history: History) -> Mapping[str, float]:
        return self._engine.compute(history)


class _SizeTomorrow(Size):
    """Size, peeking at the next session first: a lookback of 1 must still catch it."""

    name = "size_tomorrow"

    def compute(self, history: History) -> Mapping[str, float]:
        history.bar("aaa", SESSIONS[SESSIONS.index(history.as_of) + 1])
        return super().compute(history)


# --- the registry ------------------------------------------------------------


def test_the_catalogue_names_each_feature_once_with_its_declared_lookback() -> None:
    lookbacks = {item.engine.name: item.engine.lookback.sessions for item in CATALOGUE}

    assert lookbacks == {
        "momentum_12_1": 253,
        "momentum_6_1": 127,
        "volatility_60": 61,
        "liquidity_20": 20,
        "size": 1,
    }
    assert len(lookbacks) == len(CATALOGUE)
    assert all(item.inputs and item.absence and item.direction for item in CATALOGUE)
    assert entry("size").inputs == ("shares", "close_raw")

    with pytest.raises(KeyError, match="value_ep"):
        entry("value_ep")


# --- over-reach ---------------------------------------------------------------


@pytest.mark.parametrize("engine", [Momentum(formation=126), Volatility(), Liquidity()])
def test_a_twin_reading_deeper_than_it_declared_is_caught(
    tmp_path: Path, engine: FeatureEngine
) -> None:
    dataset = _dataset(tmp_path, _series("aaa", [100] * len(SESSIONS), turnover=True))

    with pytest.raises(LookbackExceededError, match="understated_"):
        evaluate(_Understated(engine), dataset, SESSIONS[-1])


def test_a_size_twin_reading_a_later_session_is_caught(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("aaa", session) for session in SESSIONS[:3]]
    dataset = _dataset(tmp_path, _series("aaa", [100, 100, 100]), rows)

    with pytest.raises(LookAheadError, match="size_tomorrow"):
        evaluate(_SizeTomorrow(), dataset, SESSIONS[1])


# --- hand-computed values ----------------------------------------------------


def test_momentum_6_1_is_the_return_from_126_to_21_sessions_back(tmp_path: Path) -> None:
    # At the 127th session: from close 0 (100) to close 105 (205).
    dataset = _dataset(tmp_path, _series("aaa", [100 + i for i in range(127)]))

    assert Momentum(formation=126).name == "momentum_6_1"
    assert evaluate(Momentum(formation=126), dataset, SESSIONS[126]) == {"aaa": pytest.approx(1.05)}


def test_volatility_is_the_sample_deviation_of_60_log_returns(tmp_path: Path) -> None:
    # 100, 110, 100, ...: sixty returns of +/- ln 1.1, mean zero.
    closes = [100 if i % 2 == 0 else 110 for i in range(61)]
    dataset = _dataset(tmp_path, _series("aaa", closes))

    assert evaluate(Volatility(), dataset, SESSIONS[60]) == {
        "aaa": pytest.approx(math.log(1.1) * math.sqrt(60 / 59))
    }


def test_liquidity_is_the_median_turnover_over_20_sessions(tmp_path: Path) -> None:
    # Turnovers 1,000 ... 20,000 over the window; an older, larger one is outside it.
    dataset = _dataset(tmp_path, _series("aaa", [999, *range(1, 21)], turnover=True))

    assert evaluate(Liquidity(), dataset, SESSIONS[20]) == {"aaa": 10_500.0}


def test_size_is_shares_times_the_raw_close(tmp_path: Path) -> None:
    # The adjusted close (100) is not the one read: close_raw is.
    rows = [SyntheticFundamentals("aaa", SESSIONS[0], values={"shares": "1000", "close_raw": "25"})]
    dataset = _dataset(tmp_path, _series("aaa", [100]), rows)

    assert evaluate(Size(), dataset, SESSIONS[0]) == {"aaa": 25_000.0}


# --- absence -----------------------------------------------------------------


def test_momentum_6_1_is_absent_without_the_start_close(tmp_path: Path) -> None:
    dataset = _dataset(
        tmp_path,
        _series("aaa", [None, *range(101, 227)]) + _series("bbb", list(range(100, 227))),
    )

    assert set(evaluate(Momentum(formation=126), dataset, SESSIONS[126])) == {"bbb"}


def test_volatility_is_absent_with_a_gap_in_the_window(tmp_path: Path) -> None:
    gapped: list[int | None] = [100] * 61
    gapped[30] = None
    dataset = _dataset(tmp_path, _series("aaa", gapped) + _series("bbb", [100] * 61))

    assert evaluate(Volatility(), dataset, SESSIONS[60]) == {"bbb": 0.0}


def test_liquidity_is_absent_with_a_missing_bar_or_an_unreported_turnover(
    tmp_path: Path,
) -> None:
    unreported = _series("bbb", [1] * 20, turnover=True)
    unreported[5] = SyntheticBar("bbb", SESSIONS[5], Decimal(1))
    bars = (
        _series("aaa", [1] * 9 + [None] + [1] * 10, turnover=True)
        + unreported
        + _series("ccc", [1] * 20, turnover=True)
    )

    assert evaluate(Liquidity(), _dataset(tmp_path, bars), SESSIONS[19]) == {"ccc": 1_000.0}


def test_size_is_absent_without_a_row_or_with_shares_absent(tmp_path: Path) -> None:
    rows = [
        SyntheticFundamentals(
            "aaa",
            SESSIONS[0],
            values={"shares": None},
            absent={"shares": "no_charter_capital"},
        ),
        SyntheticFundamentals("ccc", SESSIONS[0], values={"shares": "10", "close_raw": "2"}),
    ]
    bars = _series("aaa", [1]) + _series("bbb", [1]) + _series("ccc", [1])
    dataset = _dataset(tmp_path, bars, rows, not_collected={"bbb": "ids_capture_gap"})

    assert evaluate(Size(), dataset, SESSIONS[0]) == {"ccc": 20.0}


# --- restatement --------------------------------------------------------------


def test_a_share_restatement_changes_size_only_from_its_session(tmp_path: Path) -> None:
    rows = [
        SyntheticFundamentals(
            "aaa",
            session,
            values={"shares": "1000" if index < 2 else "1150", "close_raw": "10"},
        )
        for index, session in enumerate(SESSIONS[:4])
    ]
    dataset = _dataset(tmp_path, _series("aaa", [10] * 4), rows)

    assert [evaluate(Size(), dataset, session)["aaa"] for session in SESSIONS[:4]] == [
        10_000.0,
        10_000.0,
        11_500.0,
        11_500.0,
    ]
