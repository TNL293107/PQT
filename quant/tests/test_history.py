"""The point-in-time harness: an engine sees no later than its as-of, no deeper than it declared.

The architecture asks for a test that plants a deliberately over-reaching
feature and shows the harness catching it. A safeguard that has never been
shown to fire is not known to work; the two cheats below are that test.
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.history import (
    LookAheadError,
    LookbackExceededError,
    NonMemberScoredError,
    PointInTimeHistory,
    evaluate,
)
from personal_quant.research.protocols import History, Lookback
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 8)


def _dataset(tmp_path: Path) -> CanonicalDataset:
    # aaa trades every session; bbb has no bar on the fourth (a gap) and joins
    # the universe only from the third.
    bars = [
        SyntheticBar("aaa", session, Decimal(10 + index)) for index, session in enumerate(SESSIONS)
    ]
    bars += [
        SyntheticBar("bbb", session, Decimal(50 + index))
        for index, session in enumerate(SESSIONS)
        if index != 3
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=bars,
            spells={"aaa": [(SESSIONS[0], None)], "bbb": [(SESSIONS[2], None)]},
        ),
    )
    return LocalDatasetProvider(tmp_path).open("0123456789abcdef")


class _LastClose:
    """An honest feature: the as-of close, with a lookback of one."""

    name = "last_close"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        result: dict[str, float] = {}

        for instrument in history.members():
            (close,) = history.closes(instrument, 1)

            if close is not None:
                result[instrument] = float(close)

        return result


class _ReadsDeeperThanDeclared:
    """Declares five sessions and reads six."""

    name = "deeper_than_declared"
    version = "1"
    lookback = Lookback(5)

    def compute(self, history: History) -> Mapping[str, float]:
        closes = history.closes("aaa", 6)
        return {"aaa": float(closes[-1] or 0)}


class _PeeksAtTomorrow:
    """Reads the next session's close: the look-ahead the harness exists to catch."""

    name = "peeks_at_tomorrow"
    version = "1"
    lookback = Lookback(5)

    def compute(self, history: History) -> Mapping[str, float]:
        tomorrow = SESSIONS[SESSIONS.index(history.as_of) + 1]
        bar = history.bar("aaa", tomorrow)
        return {"aaa": float(bar.close) if bar else 0.0}


class _ScoresANonMember:
    """Scores an instrument that was not in the universe on the as-of."""

    name = "scores_a_non_member"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        return {"bbb": 1.0}


def test_closes_end_at_the_as_of_oldest_first(tmp_path: Path) -> None:
    history = PointInTimeHistory(_dataset(tmp_path), SESSIONS[5], Lookback(3))

    assert list(history.closes("aaa", 3)) == [Decimal(13), Decimal(14), Decimal(15)]


def test_a_session_without_a_bar_reads_as_none(tmp_path: Path) -> None:
    history = PointInTimeHistory(_dataset(tmp_path), SESSIONS[5], Lookback(3))

    assert list(history.closes("bbb", 3)) == [None, Decimal(54), Decimal(55)]


def test_sessions_before_the_dataset_begins_read_as_none(tmp_path: Path) -> None:
    history = PointInTimeHistory(_dataset(tmp_path), SESSIONS[1], Lookback(4))

    assert list(history.closes("aaa", 4)) == [None, None, Decimal(10), Decimal(11)]


def test_members_are_read_on_the_as_of(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)

    assert PointInTimeHistory(dataset, SESSIONS[1], Lookback(1)).members() == {"aaa"}
    assert PointInTimeHistory(dataset, SESSIONS[2], Lookback(1)).members() == {"aaa", "bbb"}


def test_an_honest_feature_is_evaluated(tmp_path: Path) -> None:
    assert evaluate(_LastClose(), _dataset(tmp_path), SESSIONS[4]) == {"aaa": 14.0, "bbb": 54.0}


def test_reading_deeper_than_the_declared_lookback_is_caught(tmp_path: Path) -> None:
    with pytest.raises(LookbackExceededError, match="deeper_than_declared"):
        evaluate(_ReadsDeeperThanDeclared(), _dataset(tmp_path), SESSIONS[6])


def test_reading_a_session_after_the_as_of_is_caught(tmp_path: Path) -> None:
    with pytest.raises(LookAheadError, match="peeks_at_tomorrow"):
        evaluate(_PeeksAtTomorrow(), _dataset(tmp_path), SESSIONS[5])


def test_reading_a_bar_older_than_the_window_is_caught(tmp_path: Path) -> None:
    history = PointInTimeHistory(_dataset(tmp_path), SESSIONS[6], Lookback(2))

    with pytest.raises(LookbackExceededError):
        history.bar("aaa", SESSIONS[4])


def test_scoring_an_instrument_outside_the_universe_is_caught(tmp_path: Path) -> None:
    # bbb joins on the third session. A score for it before then is a score for
    # a security no strategy could have chosen from the index that day.
    with pytest.raises(NonMemberScoredError, match="bbb"):
        evaluate(_ScoresANonMember(), _dataset(tmp_path), SESSIONS[1])


def test_an_as_of_that_is_not_a_session_is_refused(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="not a session"):
        PointInTimeHistory(_dataset(tmp_path), date(2026, 1, 10), Lookback(1))
