"""The Phase 7 factor report's tooling: the request it is built from and its tables.

Sprint 11.6 rebuilds what sprint 10 kept under ``tmp/`` and lost: the
labelled-dataset request (ADR-034), the read-only query of the run store and
the table renderer of ``evidence/phase-7-vn30-report.md``. Every run and
figure here is invented.
"""

from __future__ import annotations

import json
from dataclasses import replace
from datetime import date
from typing import Any

import pytest

from personal_quant.factors.catalogue import CATALOGUE
from personal_quant.research.factor_report import (
    ReportError,
    ReportRun,
    build_request,
    render_tables,
    runs_from_jsonl,
    runs_to_jsonl,
)

EXPERIMENT = "phase-7-test"
DATASET_ID = "d0d0d0d0d0d0d0d0"


# --- the request ---------------------------------------------------------------


def test_the_request_names_every_catalogue_feature_with_its_version_and_lookback() -> None:
    request = build_request(universe="VN30", from_date=date(2024, 8, 5), to_date=date(2026, 11, 13))

    expected = sorted(
        (entry.engine.name, int(entry.engine.version), entry.engine.lookback.sessions)
        for entry in CATALOGUE
    )
    assert [(f["name"], f["version"], f["lookback"]) for f in request["features"]] == expected
    assert len(request["features"]) == 8


def test_the_request_is_the_adr_034_v1_shape() -> None:
    request = build_request(universe="VN30", from_date=date(2024, 8, 5), to_date=date(2026, 11, 13))

    assert {key: value for key, value in request.items() if key != "features"} == {
        "universe": "VN30",
        "as_of": None,
        "from": "2024-08-05",
        "to": "2026-11-13",
        "interval": "1d",
        "fundamentals": True,
        "rebalance_every": 21,
        "label": {"horizon": 21, "basis": "adjusted"},
    }


def test_a_request_ending_before_it_starts_is_refused() -> None:
    with pytest.raises(ReportError, match="ends"):
        build_request(universe="VN30", from_date=date(2026, 1, 2), to_date=date(2026, 1, 2))


def test_a_horizon_other_than_the_rebalance_is_refused() -> None:
    # ADR-034 v1: labels neither overlap nor leave gaps.
    with pytest.raises(ReportError, match="horizon"):
        build_request(
            universe="VN30",
            from_date=date(2024, 8, 5),
            to_date=date(2026, 11, 13),
            horizon=42,
        )


# --- the runs ------------------------------------------------------------------


def _evaluation(
    trial: int,
    feature: str,
    transform: str,
    *,
    rank_ic: float | None = 0.05,
    status: str = "succeeded",
    job: str | None = "00000000-0000-0000-0000-00000000000j",
    reproduces: str | None = None,
) -> ReportRun:
    metrics: dict[str, float | None] = (
        {
            "periods": 24.0,
            "coverage_mean": 26.44,
            "rank_ic_mean": rank_ic,
            "rank_ic_t_statistic": 1.2345,
            "rank_ic_periods": 24.0,
            "ic_mean": -0.0125,
            "ic_t_statistic": -0.5,
            "ic_periods": 24.0,
            "spread_mean": 0.01,
            "spread_t_statistic": 2.581,
            "spread_periods": 24.0,
            "turnover_mean": 0.19861,
            "turnover_periods": 23.0,
            "decay_rank_ic_mean_h5": 0.007,
            "decay_periods_h5": 24.0,
            "decay_rank_ic_mean_h21": rank_ic,
            "decay_periods_h21": 24.0,
            "decay_rank_ic_mean_h63": None,
            "decay_periods_h63": 0.0,
        }
        if status == "succeeded"
        else {}
    )
    return ReportRun(
        run_id=f"00000000-0000-0000-0000-{trial:012d}" if reproduces is None else "r" * 36,
        experiment=EXPERIMENT,
        strategy="factor_evaluation",
        status=status,
        content_hash="a" * 64,
        factor_version=f"{feature}@1",
        params={"feature": feature, "transform": transform, "decay_horizons": [5, 21, 63]},
        code_commit="c0ffee1",
        trial_index=None if reproduces else trial,
        trial_count=None if reproduces else trial,
        reproduces_run_id=reproduces,
        job_id=job,
        metrics=metrics,
        dataset_id=DATASET_ID,
        dataset_version=1,
        schema_version=6,
    )


def _correlation(transform: str = "wz-1") -> ReportRun:
    return ReportRun(
        run_id="00000000-0000-0000-0000-0000000000cc",
        experiment=f"{EXPERIMENT}-correlation",
        strategy="factor_correlation",
        status="succeeded",
        content_hash="a" * 64,
        factor_version="catalogue@1",
        params={"transform": transform},
        code_commit="c0ffee1",
        trial_index=1,
        trial_count=1,
        reproduces_run_id=None,
        job_id="00000000-0000-0000-0000-00000000000j",
        metrics={
            "corr_size__value_bp_mean": -0.21044,
            "corr_size__value_bp_periods": 24.0,
            "corr_liquidity_20__size_mean": None,
            "corr_liquidity_20__size_periods": 0.0,
        },
        dataset_id=DATASET_ID,
        dataset_version=1,
        schema_version=6,
    )


def _runs() -> list[ReportRun]:
    return [
        _evaluation(2, "momentum_12_1", "wz-1"),
        _evaluation(1, "momentum_12_1", "raw", rank_ic=0.0784),
        _evaluation(3, "size", "raw", rank_ic=None),
        _evaluation(4, "size", "wz-1", status="failed"),
        _evaluation(1, "momentum_12_1", "raw", reproduces="00000000-0000-0000-0000-000000000001"),
        _correlation(),
    ]


def test_runs_survive_the_jsonl_round_trip() -> None:
    runs = _runs()

    assert runs_from_jsonl(runs_to_jsonl(runs)) == runs


def test_the_count_line_states_trials_reproductions_failures_and_queued_runs() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    assert (
        f"`{EXPERIMENT}`: 5 runs, made of 4 trials and 1 reproduction; "
        "latest `trial_count` 4; 1 failed; 0 without a job" in tables
    )


def test_a_run_recorded_without_a_job_is_counted() -> None:
    runs = [*_runs()[:-1], _evaluation(5, "value_ep", "raw", job=None), _correlation()]

    assert "latest `trial_count` 5; 1 failed; 1 without a job" in render_tables(runs, EXPERIMENT)


def test_rows_follow_the_catalogue_order_and_print_the_trial_count_rule() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    raw = tables.split("### `raw`")[1].split("###")[0]
    assert raw.index("momentum_12_1") < raw.index("| size")
    assert "`trial_count` = 4 for every number" in tables


def test_rows_follow_the_catalogue_not_the_order_the_trials_ran_in() -> None:
    # size ran first and momentum_12_1 last; the catalogue lists momentum first.
    runs = [
        _evaluation(1, "size", "raw"),
        _evaluation(2, "value_ep", "raw"),
        _evaluation(3, "momentum_12_1", "raw"),
    ]

    raw = render_tables(runs, EXPERIMENT).split("### `raw`")[1].split("###")[0]

    assert raw.index("| momentum_12_1") < raw.index("| size") < raw.index("| value_ep")


def test_a_row_prints_signed_means_t_values_coverage_and_periods() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    assert (
        "| 1 | momentum_12_1 | 26.4 | +0.0784 | +1.23 | -0.0125 | -0.50 | +0.0100 | +2.58 "
        "| 0.1986 | 24 / 24 / 23 |" in tables
    )


def test_an_absent_metric_and_a_failed_trial_are_said_not_left_blank() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    assert "| 3 | size | 26.4 | absent |" in tables
    assert "| 4 | size | failed |" in tables


def test_decay_prints_each_horizon_with_its_periods() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    decay = tables.split("## Decay")[1].split("## Correlation")[0]
    assert "| feature | h=5 | h=21 | h=63 |" in decay
    assert "| momentum_12_1 | +0.0070 (24) | +0.0784 (24) | absent (0) |" in decay


def test_correlation_pairs_are_in_name_order_with_absent_pairs_said() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    correlation = tables.split("## Correlation")[1].split("## Run ids")[0]
    assert "under `wz-1`" in correlation
    assert correlation.index("liquidity_20 / size | absent | 0") < correlation.index(
        "size / value_bp | -0.2104 | 24"
    )


def test_the_run_id_table_lists_trials_then_reproductions() -> None:
    tables = render_tables(_runs(), EXPERIMENT)

    ids = tables.split("## Run ids")[1]
    assert "| 1 | momentum_12_1 | raw | `00000000-0000-0000-0000-000000000001` | succeeded |" in ids
    assert ids.index("| 4 |") < ids.index("reproduces trial 1")


def test_runs_over_two_datasets_are_refused() -> None:
    other = _evaluation(5, "value_ep", "raw")
    runs = [*_runs(), replace(other, dataset_version=2)]

    with pytest.raises(ReportError, match="more than one dataset"):
        render_tables(runs, EXPERIMENT)


def test_an_experiment_with_no_runs_is_refused() -> None:
    with pytest.raises(ReportError, match="no runs"):
        render_tables([_correlation()], EXPERIMENT)


def test_jsonl_lines_are_one_object_each_with_stable_keys() -> None:
    lines = runs_to_jsonl(_runs()[:1]).splitlines()

    parsed: dict[str, Any] = json.loads(lines[0])
    assert list(parsed) == sorted(parsed)
    assert parsed["metrics"]["rank_ic_mean"] == 0.05
