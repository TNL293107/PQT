"""Fundamentals through the harness: the as-of row only, refused where unreadable (ADR-033)."""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.history import (
    FundamentalsUnavailableError,
    PointInTimeHistory,
    evaluate,
)
from personal_quant.research.protocols import History, Lookback
from synthetic import (
    SyntheticBar,
    SyntheticDataset,
    SyntheticFundamentals,
    weekdays,
    write_dataset,
)

SESSIONS = weekdays(date(2026, 1, 5), 4)


def _dataset(tmp_path: Path, fundamentals: bool = True, ratio_version: int = 2) -> CanonicalDataset:
    # aaa's EPS is restated from the third session; its ROE is absent on the
    # second. bbb is a member throughout with no fundamentals row.
    rows = [
        SyntheticFundamentals(
            "aaa",
            session,
            values={"eps_ttm": "100" if index < 2 else "120", "roe": None if index == 1 else "0.2"},
            absent={"roe": "no_balance_sheet"} if index == 1 else {},
        )
        for index, session in enumerate(SESSIONS)
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=[
                SyntheticBar(name, session, Decimal(10))
                for name in ("aaa", "bbb")
                for session in SESSIONS
            ],
            spells={"aaa": [(SESSIONS[0], None)], "bbb": [(SESSIONS[0], None)]},
            fundamentals=rows if fundamentals else None,
            not_collected={"bbb": "ids_capture_gap"} if fundamentals else {},
            ratio_version=ratio_version,
        ),
    )
    return LocalDatasetProvider(tmp_path).open("0123456789abcdef")


class _Eps:
    name = "eps"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        values: dict[str, float] = {}

        for instrument in history.members():
            row = history.fundamentals(instrument)

            if row is not None and (eps := row.values["eps_ttm"]) is not None:
                values[instrument] = float(eps)

        return values


def test_the_row_read_is_the_as_of_row_with_its_absence_codes(tmp_path: Path) -> None:
    history = PointInTimeHistory(_dataset(tmp_path), SESSIONS[1], Lookback(1))

    row = history.fundamentals("aaa")

    assert row is not None
    assert row.session == SESSIONS[1]
    assert row.values["eps_ttm"] == Decimal(100)
    assert row.values["roe"] is None
    assert row.absent["roe"] == "no_balance_sheet"
    assert row.absent["eps_ttm"] is None


def test_an_instrument_without_a_row_reads_as_none_not_zero(tmp_path: Path) -> None:
    assert (
        PointInTimeHistory(_dataset(tmp_path), SESSIONS[0], Lookback(1)).fundamentals("bbb") is None
    )


def test_a_row_is_not_filled_forward_to_a_session_without_one(tmp_path: Path) -> None:
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=[SyntheticBar("aaa", session, Decimal(10)) for session in SESSIONS[:2]],
            spells={"aaa": [(SESSIONS[0], None)]},
            fundamentals=[SyntheticFundamentals("aaa", SESSIONS[0])],
            ratio_version=2,
        ),
    )
    dataset = LocalDatasetProvider(tmp_path).open("0123456789abcdef")

    assert PointInTimeHistory(dataset, SESSIONS[0], Lookback(1)).fundamentals("aaa") is not None
    assert PointInTimeHistory(dataset, SESSIONS[1], Lookback(1)).fundamentals("aaa") is None


def test_a_restatement_changes_the_value_only_from_its_session(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)

    by_session = [evaluate(_Eps(), dataset, session) for session in SESSIONS]

    assert by_session == [{"aaa": 100.0}, {"aaa": 100.0}, {"aaa": 120.0}, {"aaa": 120.0}]


def test_a_build_without_fundamentals_is_refused(tmp_path: Path) -> None:
    with pytest.raises(FundamentalsUnavailableError, match="without --fundamentals"):
        evaluate(_Eps(), _dataset(tmp_path, fundamentals=False), SESSIONS[0])


def test_ratio_version_1_is_refused(tmp_path: Path) -> None:
    with pytest.raises(FundamentalsUnavailableError, match="carries 1"):
        evaluate(_Eps(), _dataset(tmp_path, ratio_version=1), SESSIONS[0])
