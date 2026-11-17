"""Acceptance for sprint 11.6, option A: vendor share per feature from the dataset (QA v6-M2).

ADR-038 Consequences: "Phase 7's report states the vendor share of each value
feature's inputs." Option A (Lead, 2026-11-17) puts a vendor flag on every
value of ``fundamentals.parquet`` (manifest schema 7), so that share is
computed from the dataset build alone: no database, no ratio service.

What these tests pin, and what they leave to Dev:

- Pinned: schema 7 is read and hashed, and schemas 1-6 hash as before (the
  committed fixtures, see test_dataset_manifest.py); ``v7-export.json`` hashes
  the same in both languages; the share per feature, by the definition below.
- Dev's: the flag column's name and type, and where the share function lives.
  Each is named once, in ``VENDOR_FLAG`` and ``_vendor_shares``; change those,
  not the asserts.

Share of a feature: among the rows where every fundamentals input of its
catalogue entry is present, the fraction with any of those inputs flagged
vendor. Features with no fundamentals input (momentum, volatility, liquidity)
have no share.
"""

from __future__ import annotations

import hashlib
import importlib
import json
from collections.abc import Callable, Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pyarrow as pa
import pyarrow.parquet as pq
import pytest

from personal_quant.research.datasets import FUNDAMENTAL_VALUES, LocalDatasetProvider
from personal_quant.research.manifest import (
    SUPPORTED_SCHEMA_VERSIONS,
    DatasetManifest,
    content_hash_of,
)
from synthetic import SyntheticBar, SyntheticDataset, SyntheticFundamentals, weekdays, write_dataset

FIXTURES = Path(__file__).parent / "fixtures" / "manifests"

# The flag column beside a value column. Dev may rename it; change it only here.
VENDOR_FLAG = "{}_vendor"

# Where the share is computed. Dev may move or rename it; change it only here.
_SHARE_MODULE, _SHARE_FUNCTION = "personal_quant.factors.catalogue", "vendor_shares"

SESSIONS = weekdays(date(2026, 11, 2), 2)

# Two issuers, two sessions; every value invented. The flags each row carries:
#   aaa 1: none (Circular 200 only)
#   aaa 2: the earnings values and ROE (a vendor trailing quarter)
#   bbb 1: earnings, book values and ROE (a vendor balance sheet and quarter)
#   bbb 2: ROE alone (a vendor year-earlier equity); book value absent
_EARNINGS = ("net_profit_ttm", "eps_ttm", "pe")
_BOOK = ("book_value_per_share", "pb")
ROWS: list[tuple[str, date, frozenset[str], Mapping[str, str | None]]] = [
    ("aaa", SESSIONS[0], frozenset(), {}),
    ("aaa", SESSIONS[1], frozenset({*_EARNINGS, "roe"}), {}),
    ("bbb", SESSIONS[0], frozenset({*_EARNINGS, *_BOOK, "roe"}), {}),
    ("bbb", SESSIONS[1], frozenset({"roe"}), {"book_value_per_share": None, "pb": None}),
]

EXPECTED_SHARES = {
    # shares and close_raw are never a vendor's here.
    "size": 0.0,
    # eps_ttm present on all four rows, a vendor's on aaa 2 and bbb 1.
    "value_ep": 2 / 4,
    # book value present on three rows, a vendor's on bbb 1.
    "value_bp": 1 / 3,
    # roe present on all four, a vendor's on three.
    "quality_roe": 3 / 4,
}


def _vendor_shares() -> Callable[..., Mapping[str, float]]:
    module = importlib.import_module(_SHARE_MODULE)
    function = getattr(module, _SHARE_FUNCTION, None)

    assert function is not None, (
        f"{_SHARE_MODULE}.{_SHARE_FUNCTION} does not exist: nothing computes the vendor share "
        "per feature from a dataset (11.6 option A)."
    )

    return function


Row = tuple[str, date, frozenset[str], Mapping[str, str | None]]


def _write_schema_7(root: Path, rows: list[Row]) -> None:
    """Write a schema 4 build with the synthetic writer, then make it schema 7."""
    bars = [
        SyntheticBar(name, session, Decimal(50_000))
        for name in ("aaa", "bbb")
        for session in SESSIONS
    ]
    build = write_dataset(
        root,
        SyntheticDataset(
            bars=bars,
            spells={name: [(SESSIONS[0], None)] for name in ("aaa", "bbb")},
            fundamentals=[
                SyntheticFundamentals(
                    name,
                    session,
                    values=dict(values),
                    absent={column: "no_balance_sheet" for column in values},
                )
                for name, session, _, values in rows
            ],
            ratio_version=2,
        ),
    )

    path = build / "fundamentals.parquet"
    table = pq.read_table(path)
    flags = {(name, session): flagged for name, session, flagged, _ in rows}
    keys = [
        (name, session.date() if hasattr(session, "date") else session)
        for name, session in zip(
            table.column("instrument_id").to_pylist(), table.column("session").to_pylist()
        )
    ]

    for column in FUNDAMENTAL_VALUES:
        table = table.append_column(
            VENDOR_FLAG.format(column),
            pa.array([column in flags[key] for key in keys], type=pa.bool_()),
        )

    pq.write_table(table, path)
    payload = path.read_bytes()

    raw: dict[str, Any] = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    entry = next(file for file in raw["files"] if file["name"] == "fundamentals.parquet")
    entry["bytes"], entry["sha256"] = len(payload), hashlib.sha256(payload).hexdigest()
    raw["schema_version"] = 7
    raw["fundamentals"]["vendor_rows"] = sum(1 for _, _, flagged, _ in rows if flagged)
    raw["request"] = None
    raw["content_hash"] = content_hash_of(DatasetManifest.from_json(raw))
    (build / "manifest.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")


def test_schema_7_is_a_version_this_reader_knows() -> None:
    assert 7 in SUPPORTED_SCHEMA_VERSIONS


def test_a_schema_7_manifest_hashes_and_its_vendor_rows_stay_in_the_hash() -> None:
    raw: dict[str, Any] = json.loads((FIXTURES / "v6-vendor.json").read_text(encoding="utf-8"))
    raw["schema_version"] = 7

    manifest = DatasetManifest.from_json(raw)
    edited = DatasetManifest.from_json(
        {**raw, "fundamentals": {**raw["fundamentals"], "vendor_rows": 0}}
    )

    assert manifest.schema_version == 7
    assert content_hash_of(manifest) != content_hash_of(edited)


def test_the_schema_7_fixture_hashes_to_the_content_hash_dotnet_recorded() -> None:
    # Parity: DatasetManifestGoldenTests reads the same file (GoldenV7).
    path = FIXTURES / "v7-export.json"

    assert path.exists(), "v7-export.json is not committed (write it with PQT_WRITE_GOLDEN=1)."

    raw = json.loads(path.read_text(encoding="utf-8"))
    manifest = DatasetManifest.from_json(raw)

    assert manifest.schema_version == 7
    assert content_hash_of(manifest) == raw["content_hash"]


def test_the_vendor_share_of_each_value_feature_is_read_from_the_parquet_alone(
    tmp_path: Path,
) -> None:
    _write_schema_7(tmp_path, ROWS)
    dataset = LocalDatasetProvider(tmp_path).open("0123456789abcdef")

    shares = _vendor_shares()(dataset)

    assert set(shares) == set(EXPECTED_SHARES)
    for feature, expected in EXPECTED_SHARES.items():
        assert shares[feature] == pytest.approx(expected), feature


def test_a_build_with_no_vendor_value_has_a_share_of_zero_for_every_value_feature(
    tmp_path: Path,
) -> None:
    _write_schema_7(
        tmp_path, [(name, session, frozenset(), values) for name, session, _, values in ROWS]
    )

    shares = _vendor_shares()(LocalDatasetProvider(tmp_path).open("0123456789abcdef"))

    assert shares == {feature: 0.0 for feature in EXPECTED_SHARES}
