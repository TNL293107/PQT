"""Loading a canonical dataset: located by id, verified before it is trusted."""

from __future__ import annotations

import json
from datetime import date
from decimal import Decimal
from pathlib import Path

import pytest

from personal_quant.research.datasets import DatasetIntegrityError, LocalDatasetProvider
from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
)
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 5)


def _spec(version: int = 1) -> SyntheticDataset:
    return SyntheticDataset(
        version=version,
        bars=[
            SyntheticBar("aaa", session, Decimal("100.5") + index)
            for index, session in enumerate(SESSIONS)
        ]
        + [SyntheticBar("bbb", session, Decimal("20.125")) for session in SESSIONS[2:4]],
        spells={"aaa": [(SESSIONS[0], None)], "bbb": [(SESSIONS[2], SESSIONS[4])]},
    )


def test_a_build_loads_with_exact_decimal_prices(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())

    dataset = LocalDatasetProvider(tmp_path).open("0123456789abcdef")

    assert dataset.manifest.dataset_version == 1
    assert list(dataset.sessions) == SESSIONS
    assert dataset.bars_of("aaa")[SESSIONS[1]].close == Decimal("101.5")
    assert dataset.bars_of("bbb")[SESSIONS[2]].close == Decimal("20.125")
    assert SESSIONS[0] not in dataset.bars_of("bbb")


def test_without_a_version_the_latest_build_is_opened(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec(version=1))
    write_dataset(tmp_path, _spec(version=2))

    provider = LocalDatasetProvider(tmp_path)

    assert list(provider.versions("0123456789abcdef")) == [1, 2]
    assert provider.open("0123456789abcdef").manifest.dataset_version == 2
    assert provider.open("0123456789abcdef", 1).manifest.dataset_version == 1


def test_point_in_time_membership_follows_the_spells(tmp_path: Path) -> None:
    # bbb's spell is half-open: a member from its third session, gone on the fifth.
    write_dataset(tmp_path, _spec())

    dataset = LocalDatasetProvider(tmp_path).open("0123456789abcdef")

    assert dataset.members_on(SESSIONS[1]) == {"aaa"}
    assert dataset.members_on(SESSIONS[3]) == {"aaa", "bbb"}
    assert dataset.members_on(SESSIONS[4]) == {"aaa"}


def test_an_as_of_dataset_has_every_instrument_as_a_member(tmp_path: Path) -> None:
    spec = _spec()
    spec.spells = None
    write_dataset(tmp_path, spec)

    dataset = LocalDatasetProvider(tmp_path).open("0123456789abcdef")

    assert dataset.members_on(SESSIONS[0]) == {"aaa", "bbb"}


def test_a_changed_data_file_is_refused(tmp_path: Path) -> None:
    # One byte is enough. A reproducible result computed from a silently
    # changed file is not reproducible.
    build = write_dataset(tmp_path, _spec())
    payload = bytearray((build / "bars.parquet").read_bytes())
    payload[len(payload) // 2] ^= 0xFF
    (build / "bars.parquet").write_bytes(bytes(payload))

    with pytest.raises(DatasetIntegrityError, match=r"bars\.parquet"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_an_edited_manifest_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["to_date"] = "2027-01-01"
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(DatasetIntegrityError, match="content hash"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_file_name_that_leaves_the_build_directory_is_refused(tmp_path: Path) -> None:
    # A manifest is a file an operator can edit; its names must not walk out
    # of the build's own directory.
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["files"][0]["name"] = "../../bars.parquet"
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(DatasetIntegrityError, match="not a file name"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


@pytest.mark.parametrize("dataset_id", ["../escape", "a/b", ""])
def test_a_dataset_id_that_is_not_a_plain_name_is_refused(tmp_path: Path, dataset_id: str) -> None:
    with pytest.raises(ValueError, match="dataset id"):
        LocalDatasetProvider(tmp_path).open(dataset_id)


def test_a_duplicated_row_is_refused(tmp_path: Path) -> None:
    spec = _spec()
    spec.bars.append(SyntheticBar("aaa", SESSIONS[1], Decimal(1)))
    write_dataset(tmp_path, spec)

    with pytest.raises(DatasetIntegrityError, match="two rows"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_row_outside_its_instruments_membership_is_refused(tmp_path: Path) -> None:
    # bbb's spell ends, exclusively, on the fifth session. The exporter never
    # writes a row there; a build that has one was not written by it.
    spec = _spec()
    spec.bars.append(SyntheticBar("bbb", SESSIONS[4], Decimal(1)))
    write_dataset(tmp_path, spec)

    with pytest.raises(DatasetIntegrityError, match="outside its membership"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_row_for_an_instrument_the_manifest_does_not_list_is_refused(tmp_path: Path) -> None:
    spec = _spec()
    spec.instruments = ["aaa"]
    spec.spells = {"aaa": [(SESSIONS[0], None)]}
    write_dataset(tmp_path, spec)

    with pytest.raises(DatasetIntegrityError, match="does not list"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_row_count_that_disagrees_with_the_manifest_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["files"][0]["row_count"] += 1
    manifest["content_hash"] = content_hash_of(DatasetManifest.from_json(manifest))
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(DatasetIntegrityError, match="rows"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_dataset_with_no_build_is_reported_as_missing(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match="0123456789abcdef"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")


def test_a_build_that_is_not_daily_is_refused(tmp_path: Path) -> None:
    # Sessions are dates; an intraday build has several bars per date and
    # would be read as one.
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["interval"] = "1h"
    manifest["content_hash"] = content_hash_of(DatasetManifest.from_json(manifest))
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(UnsupportedManifestError, match="1h"):
        LocalDatasetProvider(tmp_path).open("0123456789abcdef")
