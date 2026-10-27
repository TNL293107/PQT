"""Cross-sectional transforms (ADR-035): per rebalance, absent stays absent, versioned."""

from __future__ import annotations

import math

import pytest

from personal_quant.factors.transforms import TRANSFORMS, identity, winsorise_zscore


def test_wz1_on_five_names_matches_the_hand_computation() -> None:
    # Median 3; absolute deviations 2, 1, 0, 1, 97, so MAD 1 and the limit
    # 3 + 3 x 1.4826 = 7.4478. e (100) is clipped to it; the rest stand.
    clipped = [1.0, 2.0, 3.0, 4.0, 3 + 3 * 1.4826]
    mean = sum(clipped) / 5
    deviation = math.sqrt(sum((value - mean) ** 2 for value in clipped) / 4)
    expected = {
        name: (value - mean) / deviation for name, value in zip("abcde", clipped, strict=True)
    }

    result = winsorise_zscore({"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0, "e": 100.0})

    assert result.keys() == expected.keys()
    for name, value in expected.items():
        assert result[name] == pytest.approx(value, abs=1e-12)


def test_a_constant_cross_section_is_empty() -> None:
    assert winsorise_zscore(dict.fromkeys("abcdef", 7.0)) == {}


def test_fewer_than_five_names_is_empty_not_transformed() -> None:
    assert winsorise_zscore({"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0}) == {}


def test_an_absent_name_stays_absent_and_none_is_added() -> None:
    present = {"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0, "e": 5.0}

    result = winsorise_zscore(present)

    assert set(result) == set(present)
    assert "f" not in result


def test_input_order_does_not_matter() -> None:
    forward = {"a": 1.0, "b": 5.0, "c": 2.0, "d": 9.0, "e": 3.0, "f": 40.0}
    backward = dict(reversed(list(forward.items())))

    assert winsorise_zscore(forward) == pytest.approx(winsorise_zscore(backward), abs=1e-15)


def test_another_sessions_values_do_not_change_this_ones() -> None:
    # Each rebalance is transformed alone; editing one leaves the other as it was.
    sessions = {
        "t1": {"a": 1.0, "b": 2.0, "c": 3.0, "d": 4.0, "e": 5.0},
        "t2": {"a": 10.0, "b": 20.0, "c": 30.0, "d": 40.0, "e": 50.0},
    }
    before = winsorise_zscore(sessions["t1"])

    sessions["t2"]["e"] = 1e9

    assert winsorise_zscore(sessions["t1"]) == before


def test_a_zero_mad_leaves_the_outlier_unclipped() -> None:
    # Four equal values and one outlier: MAD 0, so no clip. Mean 3, deviations
    # -2 (x4) and 8, sample deviation sqrt(80 / 4).
    result = winsorise_zscore({"a": 1.0, "b": 1.0, "c": 1.0, "d": 1.0, "e": 11.0})

    assert result["e"] == pytest.approx(8 / math.sqrt(20), abs=1e-12)
    assert result["a"] == pytest.approx(-2 / math.sqrt(20), abs=1e-12)


def test_each_transform_is_named_by_the_version_a_run_records() -> None:
    values = {"a": 1.0, "b": 2.0}

    assert {"raw": identity, "wz-1": winsorise_zscore} == TRANSFORMS
    assert identity(values) == values
    assert identity(values) is not values
