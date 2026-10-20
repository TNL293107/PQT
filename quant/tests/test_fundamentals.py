"""Fundamentals in a dataset build: held to the manifest, read through DuckDB (ADR-031)."""

from __future__ import annotations

import json
from collections.abc import Callable
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from personal_quant.research.analytical import connect
from personal_quant.research.datasets import DatasetIntegrityError, LocalDatasetProvider
from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
)
from synthetic import (
    SyntheticBar,
    SyntheticDataset,
    SyntheticFundamentals,
    weekdays,
    write_dataset,
)

DATASET = "0123456789abcdef"
SESSIONS = weekdays(date(2026, 1, 5), 5)
FINDING = json.dumps(
    ["No corporate action is recorded for this issuer; the share count is not adjusted."],
    separators=(",", ":"),
)


def _spec(
    fundamentals: list[SyntheticFundamentals] | None = None,
    not_collected: dict[str, str] | None = None,
) -> SyntheticDataset:
    # aaa is a member throughout; bbb from the third session until the fifth;
    # ccc is a member throughout and has no report.
    return SyntheticDataset(
        bars=[SyntheticBar("aaa", session, Decimal("100")) for session in SESSIONS]
        + [SyntheticBar("bbb", session, Decimal("20")) for session in SESSIONS[2:4]]
        + [SyntheticBar("ccc", session, Decimal("5")) for session in SESSIONS],
        spells={
            "aaa": [(SESSIONS[0], None)],
            "bbb": [(SESSIONS[2], SESSIONS[4])],
            "ccc": [(SESSIONS[0], None)],
        },
        fundamentals=(
            fundamentals
            if fundamentals is not None
            else [SyntheticFundamentals("aaa", session) for session in SESSIONS]
            + [SyntheticFundamentals("bbb", session) for session in SESSIONS[2:4]]
        ),
        not_collected={"ccc": "ids_capture_gap"} if not_collected is None else not_collected,
    )


def _rewrite_manifest(build: Path, edit: Callable[[dict[str, Any]], None]) -> None:
    """Edit a manifest and recompute its content hash, as a careful forger would."""
    path = build / "manifest.json"
    raw: dict[str, Any] = json.loads(path.read_text(encoding="utf-8"))
    edit(raw)
    raw["content_hash"] = content_hash_of(DatasetManifest.from_json(raw))
    path.write_text(json.dumps(raw), encoding="utf-8")


def test_a_build_with_fundamentals_verifies_and_keeps_its_rows(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())

    build = LocalDatasetProvider(tmp_path).verify(DATASET)

    assert build.manifest.schema_version == 4
    assert build.fundamentals is not None
    assert build.fundamentals.num_rows == 7
    assert build.fundamentals.column("session").to_pylist()[0] == SESSIONS[0]


def test_fundamentals_never_make_an_instrument_a_member(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())

    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    assert dataset.members_on(SESSIONS[0]) == frozenset({"aaa", "ccc"})
    assert dataset.members_on(SESSIONS[4]) == frozenset({"aaa", "ccc"})


def test_a_row_on_a_session_the_instrument_was_not_a_member_is_refused(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("bbb", SESSIONS[4])]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with pytest.raises(DatasetIntegrityError, match="not a member on"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_a_row_for_an_instrument_not_collected_is_refused(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("ccc", SESSIONS[0])]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with pytest.raises(DatasetIntegrityError, match="not collected"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_an_unknown_ratio_version_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    raw = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    raw["fundamentals"]["ratio_version"] = 2
    (build / "manifest.json").write_text(json.dumps(raw), encoding="utf-8")

    with pytest.raises(UnsupportedManifestError, match="Ratio version 2"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_a_value_present_beside_its_absence_reason_is_refused(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("aaa", SESSIONS[0], absent={"pe": "trailing_loss"})]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with pytest.raises(DatasetIntegrityError, match="pe is present and also absent"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_a_value_absent_with_no_reason_is_refused(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("aaa", SESSIONS[0], values={"roe": None})]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with pytest.raises(DatasetIntegrityError, match="roe is absent with no reason"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_a_listed_fundamentals_file_missing_from_disk_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    (build / "fundamentals.parquet").unlink()

    with pytest.raises(
        DatasetIntegrityError, match=r"fundamentals\.parquet is named by the manifest"
    ):
        LocalDatasetProvider(tmp_path).verify(DATASET)


@pytest.mark.parametrize(
    ("edit", "message"),
    [
        (lambda raw: raw.__setitem__("fundamentals", None), "describes no fundamentals"),
        (
            lambda raw: raw.__setitem__(
                "files", [file for file in raw["files"] if file["name"] == "bars.parquet"]
            ),
            "names no fundamentals.parquet",
        ),
        (lambda raw: raw["fundamentals"].__setitem__("rows", 6), "holds 7 rows"),
        (
            lambda raw: raw["fundamentals"]["not_collected"].append(
                {"instrument_id": "zzz", "reason": "out_of_scope"}
            ),
            "not an exported instrument",
        ),
    ],
)
def test_a_manifest_that_disagrees_with_its_fundamentals_is_refused(
    tmp_path: Path, edit: Callable[[dict[str, Any]], None], message: str
) -> None:
    # The hash is recomputed after the edit, so only the consistency checks
    # stand between the edit and the reader.
    build = write_dataset(tmp_path, _spec())
    _rewrite_manifest(build, edit)

    with pytest.raises(DatasetIntegrityError, match=message):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_findings_that_are_not_a_json_array_of_strings_are_refused(tmp_path: Path) -> None:
    rows = [SyntheticFundamentals("aaa", SESSIONS[0], findings="capital moved")]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with pytest.raises(DatasetIntegrityError, match="findings is not a JSON array"):
        LocalDatasetProvider(tmp_path).verify(DATASET)


def test_a_build_without_fundamentals_has_none_to_read(tmp_path: Path) -> None:
    spec = _spec()
    spec.fundamentals = None
    spec.not_collected = {}
    write_dataset(tmp_path, spec)

    build = LocalDatasetProvider(tmp_path).verify(DATASET)

    assert build.manifest.schema_version == 3
    assert build.fundamentals is None

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        assert connection.execute("SELECT count(*) FROM fundamentals").fetchone() == (0,)


def test_the_fundamentals_view_reads_values_as_decimals_and_keeps_the_reasons(
    tmp_path: Path,
) -> None:
    rows = [
        SyntheticFundamentals(
            "aaa",
            SESSIONS[0],
            values={"pe": None, "shares": "100000000", "roe": "0.1250000000000000000000000001"},
            absent={"pe": "trailing_loss"},
            findings=FINDING,
        )
    ]
    write_dataset(tmp_path, _spec(fundamentals=rows))

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        found = connection.execute(
            "SELECT session, shares, pe, pe_absent, roe, findings FROM fundamentals"
        ).fetchall()

    assert found == [
        (
            SESSIONS[0],
            Decimal("100000000"),
            None,
            "trailing_loss",
            Decimal("0.1250000000"),
            FINDING,
        )
    ]
