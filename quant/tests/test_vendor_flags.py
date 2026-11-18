"""Reading the per-value vendor flags of a schema 7 build (11.6 option A, ADR-038).

QA's acceptance (test_vendor_share_acceptance.py) pins the share per feature;
these pin how the reader holds the flags to the build. Every value invented.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import pyarrow as pa
import pyarrow.parquet as pq
import pytest

from personal_quant.factors.catalogue import vendor_shares
from personal_quant.research.datasets import (
    FUNDAMENTAL_VALUES,
    DatasetIntegrityError,
    LocalDatasetProvider,
)
from personal_quant.research.manifest import DatasetManifest, content_hash_of
from synthetic import SyntheticBar, SyntheticDataset, SyntheticFundamentals, write_dataset
from test_vendor_share_acceptance import ROWS, SESSIONS, VENDOR_FLAG, Row, _write_schema_7

DATASET = "0123456789abcdef"


def _rewrite_fundamentals(root: Path, edit: object) -> None:
    """Apply ``edit`` to the build's fundamentals table and re-seal the manifest."""
    import hashlib
    import json

    from personal_quant.research.manifest import DatasetManifest, content_hash_of

    build = next(root.glob("*/v1"))
    path = build / "fundamentals.parquet"
    table = edit(pq.read_table(path))  # type: ignore[operator]
    pq.write_table(table, path)
    payload = path.read_bytes()
    raw = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    entry = next(file for file in raw["files"] if file["name"] == "fundamentals.parquet")
    entry["bytes"], entry["sha256"] = len(payload), hashlib.sha256(payload).hexdigest()
    raw["content_hash"] = content_hash_of(DatasetManifest.from_json(raw))
    (build / "manifest.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")


def test_a_row_carries_the_values_flagged_vendor(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    read = sorted(
        (sorted(row.vendor), row.session)
        for instrument in dataset.manifest.instruments
        for row in dataset.fundamentals_of(instrument.instrument_id).values()
    )

    assert read == sorted((sorted(flagged), session) for _, session, flagged, _ in ROWS)


def test_a_schema_7_build_missing_a_flag_column_is_refused(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    _rewrite_fundamentals(tmp_path, lambda table: table.drop_columns([VENDOR_FLAG.format("roe")]))

    with pytest.raises(DatasetIntegrityError, match="roe_vendor"):
        LocalDatasetProvider(tmp_path).open(DATASET)


def test_a_vendor_flag_on_an_absent_value_is_read_and_shares_nothing(tmp_path: Path) -> None:
    # bbb's second row has no book value, yet a vendor input was read for it
    # (the .NET writer flags what a value rests on, not whether it came out).
    rows = [
        (name, session, flagged | ({"pb"} if values else frozenset()), values)
        for name, session, flagged, values in ROWS
    ]
    _write_schema_7(tmp_path, rows)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    flagged_absent = [
        row
        for instrument in dataset.manifest.instruments
        for row in dataset.fundamentals_of(instrument.instrument_id).values()
        if "pb" in row.vendor and row.values["pb"] is None
    ]

    assert len(flagged_absent) == 1
    # The share counts rows whose inputs are present, so the flag adds nothing.
    assert vendor_shares(dataset)["value_bp"] == pytest.approx(1 / 3)


def test_a_null_flag_is_refused(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    column = VENDOR_FLAG.format("eps_ttm")

    def nulled(table: pa.Table) -> pa.Table:
        index = table.schema.get_field_index(column)
        return table.set_column(index, column, pa.array([None] * table.num_rows, type=pa.bool_()))

    _rewrite_fundamentals(tmp_path, nulled)

    with pytest.raises(DatasetIntegrityError, match="eps_ttm_vendor"):
        LocalDatasetProvider(tmp_path).open(DATASET)


def test_a_build_before_schema_7_has_no_vendor_share(tmp_path: Path) -> None:
    # Schema 4 here: no flags were written, so a share of 0 would be a claim.
    write_dataset(
        tmp_path,
        SyntheticDataset(
            bars=[SyntheticBar("aaa", session, 50_000) for session in SESSIONS],  # type: ignore[arg-type]
            spells={"aaa": [(SESSIONS[0], None)]},
            fundamentals=[
                SyntheticFundamentals("aaa", session, values={}, absent={}) for session in SESSIONS
            ],
            ratio_version=2,
        ),
    )
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    assert all(
        not row.vendor
        for row in dataset.fundamentals_of(dataset.manifest.instruments[0].instrument_id).values()
    )

    with pytest.raises(ValueError, match="schema 7"):
        vendor_shares(dataset)


def test_every_value_column_has_its_flag_named_once() -> None:
    assert [VENDOR_FLAG.format(name) for name in FUNDAMENTAL_VALUES] == [
        f"{name}_vendor" for name in FUNDAMENTAL_VALUES
    ]


# --- QA + QC, test gaps from the PR #26 gate (Tester's survivors a and b) -----


def test_a_feature_whose_input_is_present_on_no_row_has_a_share_of_nan(tmp_path: Path) -> None:
    # quality_roe reads roe alone. With roe absent on every row there is nothing
    # to share, so the share is nan, not 0.0: 0.0 would claim "measured, none
    # vendor" for a feature that was never measured (survivor a).
    rows: list[Row] = [
        (name, session, flagged, {**values, "roe": None}) for name, session, flagged, values in ROWS
    ]
    _write_schema_7(tmp_path, rows)

    share = vendor_shares(LocalDatasetProvider(tmp_path).open(DATASET))["quality_roe"]

    assert math.isnan(share)


def test_flag_columns_in_a_schema_6_build_are_not_read(tmp_path: Path) -> None:
    # Flags exist from schema 7. A schema 6 build that happens to carry flag
    # columns (set True here) must still read as unflagged: the verifier never
    # held those columns to the build. A reader gating on ">= 6" reads them
    # (survivor b).
    _write_schema_7(tmp_path, ROWS)
    _rewrite_fundamentals(tmp_path, lambda table: table)  # re-seal before the schema edit
    build = next(tmp_path.glob("*/v1"))
    raw = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    raw["schema_version"] = 6
    raw["content_hash"] = content_hash_of(DatasetManifest.from_json(raw))
    (build / "manifest.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")

    dataset = LocalDatasetProvider(tmp_path).open(DATASET)
    rows = [
        row
        for instrument in dataset.manifest.instruments
        for row in dataset.fundamentals_of(instrument.instrument_id).values()
    ]

    assert any(flagged for _, _, flagged, _ in ROWS)  # the columns do carry True flags
    assert rows
    assert all(not row.vendor for row in rows)
