"""Labelled datasets (ADR-034 §4, §6): read through the request, refused where it lies."""

from __future__ import annotations

import json
from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import DatasetIntegrityError, LocalDatasetProvider
from personal_quant.research.labelled import LabelledDataset, LabelledDatasetError
from personal_quant.research.protocols import History, Lookback
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 12)
DATASET = "0123456789abcdef"


class _LastClose:
    name = "last_close"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        values: dict[str, float] = {}

        for instrument in history.members():
            (close,) = history.closes(instrument, 1)

            if close is not None:
                values[instrument] = float(close)

        return values


MOMENTUM = Momentum(formation=2, skip=0)


def _request(**changes: object) -> dict[str, Any]:
    request: dict[str, Any] = {
        "universe": "TEST30",
        "as_of": None,
        "from": SESSIONS[2].isoformat(),
        "to": SESSIONS[-1].isoformat(),
        "interval": "1d",
        "fundamentals": False,
        "rebalance_every": 3,
        "features": [
            {"name": "momentum_2_0", "version": 1, "lookback": 3},
            {"name": "last_close", "version": 1, "lookback": 1},
        ],
        "label": {"horizon": 3, "basis": "adjusted"},
    }
    request.update(changes)
    return request


def _write(tmp_path: Path, request: Mapping[str, Any] | None = None) -> Path:
    # aaa closes 100, 101, ...; bbb is a member until the eighth session.
    bars = [
        SyntheticBar("aaa", session, Decimal(100 + index)) for index, session in enumerate(SESSIONS)
    ]
    bars += [SyntheticBar("bbb", session, Decimal(50)) for session in SESSIONS[:8]]

    return write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=bars,
            spells={"aaa": [(SESSIONS[0], None)], "bbb": [(SESSIONS[0], SESSIONS[8])]},
            request=_request() if request is None else request,
        ),
    )


def _open(tmp_path: Path) -> LabelledDataset:
    return LabelledDataset.open(LocalDatasetProvider(tmp_path), DATASET, [MOMENTUM, _LastClose()])


def test_rebalances_fall_every_rebalance_every_sessions_from_the_first_scored_one(
    tmp_path: Path,
) -> None:
    # Sessions 2, 5, 8 have a whole label period; 11 would exit past the end.
    _write(tmp_path)

    rebalances = list(_open(tmp_path).rebalances())

    assert [rebalance.session for rebalance in rebalances] == [
        SESSIONS[2],
        SESSIONS[5],
        SESSIONS[8],
    ]
    assert [rebalance.exit for rebalance in rebalances] == [SESSIONS[5], SESSIONS[8], SESSIONS[11]]


def test_a_rebalance_whose_label_ends_past_the_last_session_is_dropped_not_refused(
    tmp_path: Path,
) -> None:
    # `to` is the last session. From the third, every three: 2, 5, 8 and 11;
    # 11 + 3 is past the twelfth session, so 11 is dropped and not counted.
    _write(tmp_path, _request(to=SESSIONS[-1].isoformat()))
    labelled = _open(tmp_path)

    assert SESSIONS[11] not in [rebalance.session for rebalance in labelled.rebalances()]
    assert labelled.window().rebalances == 3
    assert labelled.window().last == SESSIONS[8]


def test_a_rebalance_whose_label_ends_one_session_past_the_end_is_dropped(
    tmp_path: Path,
) -> None:
    # From the first session, every three: 0, 3, 6, 9. Nine plus three is
    # twelve, one past the last index, so 9 is dropped.
    _write(tmp_path, _request(**{"from": SESSIONS[0].isoformat()}))
    labelled = _open(tmp_path)

    assert [rebalance.session for rebalance in labelled.rebalances()] == [
        SESSIONS[0],
        SESSIONS[3],
        SESSIONS[6],
    ]
    assert labelled.window().rebalances == 3


def test_a_member_without_a_bar_at_the_rebalance_has_no_label_not_zero(tmp_path: Path) -> None:
    # ccc is a member throughout but has no bar on the third session.
    bars = [SyntheticBar("aaa", session, Decimal(100)) for session in SESSIONS]
    bars += [
        SyntheticBar("ccc", session, Decimal(10)) for session in SESSIONS if session != SESSIONS[2]
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=bars,
            spells={"aaa": [(SESSIONS[0], None)], "ccc": [(SESSIONS[0], None)]},
            request=_request(),
        ),
    )

    first = next(_open(tmp_path).rebalances())

    assert first.session == SESSIONS[2]
    assert first.label == {"aaa": 0.0}


def test_features_and_labels_are_computed_at_read_time(tmp_path: Path) -> None:
    _write(tmp_path)

    first, second, third = _open(tmp_path).rebalances()

    assert first.features["last_close"] == {"aaa": 102.0, "bbb": 50.0}
    assert first.features["momentum_2_0"] == {"aaa": pytest.approx(2 / 100), "bbb": 0.0}
    assert first.label == {"aaa": pytest.approx(3 / 102), "bbb": 0.0}
    # bbb leaves at the eighth session: valued at its last member close.
    assert second.label["bbb"] == 0.0
    assert set(third.features["last_close"]) == {"aaa"}


def test_the_window_states_its_rebalances_and_names(tmp_path: Path) -> None:
    _write(tmp_path)

    window = _open(tmp_path).window()

    assert (window.first, window.last, window.rebalances) == (SESSIONS[2], SESSIONS[8], 3)
    assert (window.names_min, window.names_median, window.names_max) == (1, 2, 2)
    assert "exploratory" in window.statement()


def test_without_engines_the_request_is_checked_against_the_catalogue(tmp_path: Path) -> None:
    # momentum_6_1 is catalogued with a lookback of 127, so a request that
    # says 126 is refused by the default, as one naming last_close is.
    _write(tmp_path, _request(features=[{"name": "momentum_6_1", "version": 1, "lookback": 126}]))

    with pytest.raises(LabelledDatasetError, match="lookback of 126"):
        LabelledDataset.open(LocalDatasetProvider(tmp_path), DATASET)

    _write(tmp_path / "other", _request())

    with pytest.raises(LabelledDatasetError, match="last_close"):
        LabelledDataset.open(LocalDatasetProvider(tmp_path / "other"), DATASET)


def test_a_build_without_a_request_is_refused(tmp_path: Path) -> None:
    write_dataset(
        tmp_path,
        SyntheticDataset(bars=[SyntheticBar("aaa", session, Decimal(1)) for session in SESSIONS]),
    )

    with pytest.raises(LabelledDatasetError, match="no request"):
        _open(tmp_path)


@pytest.mark.parametrize(
    ("feature", "match"),
    [
        ({"name": "unknown", "version": 1, "lookback": 1}, "not known"),
        ({"name": "last_close", "version": 2, "lookback": 1}, "version 2"),
        ({"name": "last_close", "version": 1, "lookback": 5}, "lookback of 5"),
    ],
)
def test_a_request_that_disagrees_with_the_registry_is_refused(
    tmp_path: Path, feature: dict[str, Any], match: str
) -> None:
    _write(tmp_path, _request(features=[feature]))

    with pytest.raises(LabelledDatasetError, match=match):
        _open(tmp_path)


def test_an_edited_request_breaks_the_hash(tmp_path: Path) -> None:
    build = _write(tmp_path)
    path = build / "manifest.json"
    raw = json.loads(path.read_text(encoding="utf-8"))
    raw["request"]["rebalance_every"] = 5
    path.write_text(json.dumps(raw), encoding="utf-8")

    with pytest.raises(DatasetIntegrityError, match="content hash"):
        _open(tmp_path)
