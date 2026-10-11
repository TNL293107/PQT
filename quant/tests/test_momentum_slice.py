"""The vertical slice end to end: dataset → momentum → rank → deciles → returns → IC/IR."""

from __future__ import annotations

import os
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.momentum_slice import (
    SliceRefusedError,
    render,
    run_momentum_slice,
)
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2025, 1, 6), 40)
NAMES = [f"inst{index:02d}" for index in range(10)]
FACTOR = Momentum(formation=10, skip=2)


def _price(rank: int, step: int, turn: int | None = None) -> Decimal:
    # Compounds at 0.1% x rank per session; after `turn`, falls at the same
    # rate, so the names that led the formation window lead the decline.
    rate = Decimal(rank) / Decimal(1000)

    if turn is None or step <= turn:
        growth = (Decimal(1) + rate) ** step
    else:
        growth = (Decimal(1) + rate) ** turn * (Decimal(1) - rate) ** (step - turn)

    return (Decimal(100) * growth).quantize(Decimal("0.000001"))


def _write(
    tmp_path: Path,
    *,
    turn: int | None = None,
    drop: set[tuple[str, int]] | None = None,
    spells: dict[str, list[tuple[date, date | None]]] | None = None,
    as_of: bool = False,
    adjusted: bool = True,
) -> CanonicalDataset:
    membership = spells or {name: [(SESSIONS[0], None)] for name in NAMES}
    bars = [
        SyntheticBar(name, session, _price(rank, step, turn))
        for rank, name in enumerate(NAMES)
        for step, session in enumerate(SESSIONS)
        if (name, step) not in (drop or set())
        and any(
            start <= session and (until is None or session < until)
            for start, until in membership[name]
        )
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(bars=bars, spells=None if as_of else membership, adjusted=adjusted),
    )
    return LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_perfect_predictor_scores_an_ic_of_one_every_period(tmp_path: Path) -> None:
    result = run_momentum_slice(_write(tmp_path), FACTOR, quantiles=5, rebalance_every=5)

    assert result.periods
    assert all(period.information_coefficient == pytest.approx(1.0) for period in result.periods)
    assert result.information.mean == pytest.approx(1.0)


def test_a_reversal_after_the_formation_window_scores_an_ic_of_minus_one(
    tmp_path: Path,
) -> None:
    # Prices rise until session 12 and fall after it. At the first rebalance
    # (session 10) the formation window, sessions 0 to 8, is all rise; the
    # holding period, 10 to 15, is mostly fall. A slice that read the window
    # or the holding period from the wrong sessions would not see -1 here.
    result = run_momentum_slice(_write(tmp_path, turn=12), FACTOR, quantiles=5, rebalance_every=5)

    assert result.periods[0].information_coefficient == pytest.approx(-1.0)
    assert result.periods[0].spread is not None
    assert result.periods[0].spread < 0


def test_rebalances_start_once_a_full_lookback_exists_and_cover_whole_periods(
    tmp_path: Path,
) -> None:
    # Lookback 11 → the first rebalance is the eleventh session (index 10);
    # every five sessions after it, while a whole period still follows.
    result = run_momentum_slice(_write(tmp_path), FACTOR, quantiles=5, rebalance_every=5)

    assert [period.start for period in result.periods] == [
        SESSIONS[index] for index in range(10, 35, 5)
    ]
    assert result.periods[-1].end == SESSIONS[35]


def test_the_top_quantile_beats_the_bottom_when_the_signal_is_right(tmp_path: Path) -> None:
    result = run_momentum_slice(_write(tmp_path), FACTOR, quantiles=5, rebalance_every=5)

    for period in result.periods:
        assert period.bucket_returns[-1] > period.bucket_returns[0]
        assert period.spread == pytest.approx(period.bucket_returns[-1] - period.bucket_returns[0])


def test_a_leaver_is_valued_and_a_name_missing_its_entry_bar_is_left_out(
    tmp_path: Path,
) -> None:
    # inst09 leaves the universe on session 15, inside the first period
    # (sessions 10 to 15), and is valued at its last close, session 14.
    # inst08 has no bar on session 10, so no entry price and no return; it
    # is still a member and still scored. The top tercile is inst07, inst08
    # and inst09, so its return is the mean of inst07's and the leaver's —
    # and would be inst07's alone if the leaver had been dropped.
    dataset = _write(
        tmp_path,
        drop={("inst08", 10)},
        spells={
            **{name: [(SESSIONS[0], None)] for name in NAMES[:9]},
            "inst09": [(SESSIONS[0], SESSIONS[15])],
        },
    )

    first = run_momentum_slice(dataset, FACTOR, quantiles=3, rebalance_every=5).periods[0]

    def held(rank: int, last: int) -> float:
        return float(_price(rank, last) / _price(rank, 10)) - 1.0

    assert (first.members, first.scored) == (10, 10)
    assert first.top == pytest.approx((held(7, 15) + held(9, 14)) / 2)


def test_the_run_records_what_it_was_computed_from(tmp_path: Path) -> None:
    # Enough to reproduce it, and to tell what kind of build it read.
    dataset = _write(tmp_path)
    result = run_momentum_slice(dataset, FACTOR, quantiles=5, rebalance_every=5)

    assert result.dataset_id == "0123456789abcdef"
    assert result.dataset_version == 1
    assert result.content_hash == dataset.manifest.content_hash
    assert (result.membership, result.adjusted) == ("point_in_time", True)
    assert result.announcement_policy == "strict"
    assert result.known_as_of is None
    assert (result.factor, result.factor_version) == ("momentum_10_2", "1")
    assert (result.quantiles, result.rebalance_every) == (5, 5)


def test_the_report_names_the_build_and_the_ic(tmp_path: Path) -> None:
    report = render(run_momentum_slice(_write(tmp_path), FACTOR, quantiles=5, rebalance_every=5))

    assert "0123456789abcdef v1" in report
    assert "point_in_time, adjusted, strict announcements" in report
    assert "momentum_10_2" in report
    assert "IC mean" in report
    assert report.isascii() or "—" not in report


def test_an_as_of_build_is_refused(tmp_path: Path) -> None:
    # Every name in it survived to its as-of; an IC over it is survivorship.
    with pytest.raises(SliceRefusedError, match="as-of"):
        run_momentum_slice(_write(tmp_path, as_of=True), FACTOR, quantiles=5)


def test_a_raw_build_is_refused(tmp_path: Path) -> None:
    with pytest.raises(SliceRefusedError, match="raw"):
        run_momentum_slice(_write(tmp_path, adjusted=False), FACTOR, quantiles=5)


@pytest.mark.parametrize(("quantiles", "every"), [(0, 5), (5, 0)])
def test_parameters_that_produce_nothing_are_refused(
    tmp_path: Path, quantiles: int, every: int
) -> None:
    with pytest.raises(ValueError, match="at least one"):
        run_momentum_slice(_write(tmp_path), FACTOR, quantiles=quantiles, rebalance_every=every)


def test_a_dataset_too_short_for_the_lookback_has_no_periods(tmp_path: Path) -> None:
    result = run_momentum_slice(
        _write(tmp_path), Momentum(formation=60, skip=2), quantiles=5, rebalance_every=5
    )

    assert result.periods == ()
    assert result.information.periods == 0


@pytest.mark.skipif(
    not os.environ.get("PQT_DATASET_ROOT") or not os.environ.get("PQT_DATASET_ID"),
    reason="Set PQT_DATASET_ROOT and PQT_DATASET_ID to run the slice over a real export; "
    "real exports are not committed because their sources grant no licence to redistribute.",
)
def test_the_slice_runs_over_a_real_export() -> None:
    dataset = LocalDatasetProvider(Path(os.environ["PQT_DATASET_ROOT"])).open(
        os.environ["PQT_DATASET_ID"]
    )

    result = run_momentum_slice(dataset)

    assert result.periods
    assert result.information.periods == len(result.periods)
    assert all(period.members == len(dataset.members_on(period.start)) for period in result.periods)
    assert all(len(period.bucket_returns) == result.quantiles for period in result.periods)
