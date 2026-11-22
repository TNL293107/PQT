"""The report's vendor share table (sprint 11.6, ADR-038 amendment, PLAN step 11).

Built from a schema 7 build on disk with no database: the synthetic build of
test_vendor_share_acceptance.py, every value invented.
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date
from pathlib import Path

import pytest

from personal_quant.factors.catalogue import CATALOGUE, vendor_shares
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.factor_report import render_vendor_shares
from synthetic import SyntheticBar, SyntheticDataset, SyntheticFundamentals, write_dataset
from test_vendor_share_acceptance import ROWS, SESSIONS, _write_schema_7

DATASET = "0123456789abcdef"

_VALUE_FEATURES = ["size", "value_ep", "value_bp", "quality_roe"]


def _table_rows(text: str) -> list[list[str]]:
    lines = [line for line in text.splitlines() if line.startswith("| ") and "---" not in line]
    return [[cell.strip() for cell in line.strip("|").split("|")] for line in lines[1:]]


def test_the_table_has_one_row_per_value_feature_in_catalogue_order(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    rows = _table_rows(render_vendor_shares(dataset))

    catalogue = [entry.engine.name for entry in CATALOGUE]
    assert [row[0] for row in rows] == [name for name in catalogue if name in _VALUE_FEATURES]


def test_each_share_is_vendor_shares_own_number(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)
    shares = vendor_shares(dataset)

    rows = {row[0]: row for row in _table_rows(render_vendor_shares(dataset))}

    for feature, share in shares.items():
        assert rows[feature][-1] == f"{share:.4f}"

    # The inputs are named, so a reader sees what each share is a share of.
    assert rows["value_bp"][1] == "book_value_per_share, close_raw"


def test_a_feature_whose_inputs_are_present_on_no_row_says_so(tmp_path: Path) -> None:
    # No row has a book value, so value_bp has nothing to share: "—", never 0.
    rows: list[tuple[str, date, frozenset[str], Mapping[str, str | None]]] = [
        (name, session, flagged - {"book_value_per_share", "pb"}, {"book_value_per_share": None})
        for name, session, flagged, _ in ROWS
    ]
    _write_schema_7(tmp_path, rows)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)

    table = {row[0]: row for row in _table_rows(render_vendor_shares(dataset))}

    assert table["value_bp"][-1] == "—"
    assert "nan" not in render_vendor_shares(dataset)


def test_the_heading_names_the_build_the_shares_were_read_from(tmp_path: Path) -> None:
    _write_schema_7(tmp_path, ROWS)
    dataset = LocalDatasetProvider(tmp_path).open(DATASET)
    manifest = dataset.manifest

    text = render_vendor_shares(dataset)

    assert f"`{manifest.dataset_id}` v{manifest.dataset_version}" in text
    assert manifest.content_hash in text
    assert f"schema {manifest.schema_version}" in text
    fundamentals = manifest.fundamentals
    assert fundamentals is not None
    assert f"{fundamentals.vendor_rows} of {fundamentals.rows}" in text


def test_a_build_before_schema_7_is_refused_not_shown_as_zero(tmp_path: Path) -> None:
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

    # vendor_shares' own ValueError, raised through: no table, no "—" row.
    with pytest.raises(ValueError, match="schema 7"):
        render_vendor_shares(dataset)
