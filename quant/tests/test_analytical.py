"""The DuckDB view of a build: the verified bytes, exactly as Python reads them."""

from __future__ import annotations

import json
from collections.abc import Iterator
from datetime import date, datetime
from decimal import Decimal
from pathlib import Path

import duckdb
import pytest

from personal_quant.research.analytical import PRICE_TYPE, UnrepresentablePriceError, connect
from personal_quant.research.datasets import (
    CanonicalDataset,
    DatasetIntegrityError,
    LocalDatasetProvider,
)
from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
)
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

DATASET = "0123456789abcdef"
SESSIONS = weekdays(date(2026, 1, 5), 6)


def _spec() -> SyntheticDataset:
    # Six decimals is the most the exporter writes; 0.1 and 0.2 are the
    # classic values a binary float cannot hold.
    return SyntheticDataset(
        bars=[
            SyntheticBar("aaa", session, Decimal("100.123456") + Decimal("0.1") * index)
            for index, session in enumerate(SESSIONS)
        ]
        + [SyntheticBar("bbb", session, Decimal("0.2"), volume=7) for session in SESSIONS[1:5]]
        + [SyntheticBar("ccc", session, Decimal(25_000)) for session in SESSIONS[3:]],
        spells={
            "aaa": [(SESSIONS[0], None)],
            "bbb": [(SESSIONS[1], SESSIONS[5])],
            "ccc": [(SESSIONS[3], None)],
        },
    )


def _load(root: Path) -> CanonicalDataset:
    return LocalDatasetProvider(root).open(DATASET)


@pytest.fixture
def point_in_time(tmp_path: Path) -> Iterator[duckdb.DuckDBPyConnection]:
    write_dataset(tmp_path, _spec())

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        yield connection


def test_every_bar_matches_the_python_loader_exactly(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())
    dataset = _load(tmp_path)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        rows = connection.sql(
            "SELECT instrument_id, session, open, high, low, close, volume "
            "FROM bars ORDER BY instrument_id, session"
        ).fetchall()

    expected = [
        (instrument, bar.session, bar.open, bar.high, bar.low, bar.close, bar.volume)
        for instrument in ("aaa", "bbb", "ccc")
        for bar in sorted(dataset.bars_of(instrument).values(), key=lambda bar: bar.session)
    ]

    assert len(rows) == len(expected) == 13
    assert rows == expected
    assert all(type(price) is Decimal for row in rows for price in row[2:6])


def test_aggregates_stay_decimal(point_in_time: duckdb.DuckDBPyConnection) -> None:
    # 0.2 four times over is 0.8 in decimal and 0.8000000000000002-ish in
    # binary floating point; the sum is computed in the database.
    total = point_in_time.sql("SELECT sum(close) FROM bars WHERE instrument_id = 'bbb'").fetchone()

    assert total == (Decimal("0.8"),)


def test_prices_are_decimal_columns_not_doubles(
    point_in_time: duckdb.DuckDBPyConnection,
) -> None:
    columns = dict(
        point_in_time.sql(
            "SELECT column_name, data_type FROM information_schema.columns "
            "WHERE table_name = 'bars'"
        ).fetchall()
    )

    assert PRICE_TYPE == "DECIMAL(38,10)"
    assert {columns[price] for price in ("open", "high", "low", "close")} == {PRICE_TYPE}
    assert columns["session"] == "DATE"
    assert columns["volume"] == "BIGINT"


def test_sessions_are_the_loaders_sessions(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())
    dataset = _load(tmp_path)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        sessions = connection.sql("SELECT DISTINCT session FROM bars ORDER BY session").fetchall()

    assert [session for (session,) in sessions] == list(dataset.sessions) == SESSIONS


def test_a_zoned_timestamp_is_read_as_its_utc_date(tmp_path: Path) -> None:
    # The date is taken in Arrow, before DuckDB sees the column, so neither
    # DuckDB's session time zone nor the machine's can move it.
    spec = _spec()
    spec.opened_at_zone = "UTC"
    write_dataset(tmp_path, spec)
    dataset = _load(tmp_path)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        sessions = connection.sql("SELECT DISTINCT session FROM bars ORDER BY session").fetchall()

    assert [session for (session,) in sessions] == list(dataset.sessions) == SESSIONS


def _members_on(connection: duckdb.DuckDBPyConnection, session: date) -> frozenset[str]:
    rows = connection.execute(
        "SELECT instrument_id FROM membership "
        "WHERE start <= $session AND (until IS NULL OR $session < until)",
        {"session": session},
    ).fetchall()

    return frozenset(instrument for (instrument,) in rows)


@pytest.mark.parametrize("point_in_time_build", [True, False])
def test_membership_agrees_with_the_loader(tmp_path: Path, point_in_time_build: bool) -> None:
    spec = _spec()

    if not point_in_time_build:
        spec.spells = None

    write_dataset(tmp_path, spec)
    dataset = _load(tmp_path)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        for session in SESSIONS:
            assert _members_on(connection, session) == dataset.members_on(session), session


def test_point_in_time_membership_is_the_manifests_spells(
    point_in_time: duckdb.DuckDBPyConnection,
) -> None:
    rows = point_in_time.sql(
        "SELECT instrument_id, start, until, announced_on FROM membership ORDER BY instrument_id"
    ).fetchall()

    assert rows == [
        ("aaa", SESSIONS[0], None, None),
        ("bbb", SESSIONS[1], SESSIONS[5], None),
        ("ccc", SESSIONS[3], None, None),
    ]


def test_an_as_of_build_has_one_open_spell_per_instrument_from_the_window_start(
    tmp_path: Path,
) -> None:
    spec = _spec()
    spec.spells = None
    write_dataset(tmp_path, spec)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        rows = connection.sql(
            "SELECT instrument_id, start, until FROM membership ORDER BY instrument_id"
        ).fetchall()

    assert rows == [(instrument, SESSIONS[0], None) for instrument in ("aaa", "bbb", "ccc")]


def test_the_manifest_view_names_the_build(tmp_path: Path) -> None:
    write_dataset(tmp_path, _spec())
    manifest = _load(tmp_path).manifest

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        cursor = connection.sql("SELECT * FROM manifest")
        names = [column[0] for column in cursor.description]
        rows = cursor.fetchall()

    assert len(rows) == 1
    assert dict(zip(names, rows[0], strict=True)) == {
        "dataset_id": DATASET,
        "dataset_version": 1,
        "content_hash": manifest.content_hash,
        "universe_code": "TEST30",
        "membership": "point_in_time",
        "adjusted": True,
        "announcement_policy": "strict",
        "known_as_of_utc": None,
        "from_date": SESSIONS[0],
        "to_date": SESSIONS[-1],
        "history_sessions": 0,
    }


def test_a_known_as_of_instant_is_given_in_utc(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["known_as_of_utc"] = "2026-01-10T10:04:05+07:00"
    manifest["content_hash"] = content_hash_of(DatasetManifest.from_json(manifest))
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        known = connection.sql("SELECT known_as_of_utc FROM manifest").fetchone()

    assert known == (datetime(2026, 1, 10, 3, 4, 5),)


def test_a_tampered_build_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    payload = bytearray((build / "bars.parquet").read_bytes())
    payload[len(payload) // 2] ^= 0xFF
    (build / "bars.parquet").write_bytes(bytes(payload))

    with pytest.raises(DatasetIntegrityError, match=r"bars\.parquet"):
        connect(LocalDatasetProvider(tmp_path), DATASET)


def test_the_connection_cannot_read_a_file(tmp_path: Path) -> None:
    # A query that reads the build by path would read whatever is on disk
    # now, not what was verified. The connection cannot reach the disk at
    # all, and cannot be told to.
    build = write_dataset(tmp_path, _spec())
    path = (build / "bars.parquet").as_posix()

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        with pytest.raises(duckdb.PermissionException):
            connection.execute("SELECT count(*) FROM read_parquet($path)", {"path": path})

        with pytest.raises(duckdb.Error, match="configuration"):
            connection.execute("SET enable_external_access = true")


def test_a_price_decimal_38_10_would_round_is_refused(tmp_path: Path) -> None:
    # DuckDB rounds a string with an eleventh decimal into DECIMAL(38,10)
    # without complaint; the view must not differ from the file in silence.
    spec = _spec()
    spec.bars.append(SyntheticBar("ccc", SESSIONS[2], Decimal("1.00000000001")))
    spec.spells = {**(spec.spells or {}), "ccc": [(SESSIONS[2], None)]}
    write_dataset(tmp_path, spec)

    with pytest.raises(UnrepresentablePriceError, match=r"1\.00000000001"):
        connect(LocalDatasetProvider(tmp_path), DATASET)


def test_trailing_zeros_beyond_the_scale_are_not_precision(tmp_path: Path) -> None:
    spec = _spec()
    spec.bars.append(SyntheticBar("ccc", SESSIONS[2], Decimal("1.500000000000")))
    spec.spells = {**(spec.spells or {}), "ccc": [(SESSIONS[2], None)]}
    write_dataset(tmp_path, spec)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        close = connection.sql(
            "SELECT close FROM bars WHERE instrument_id = 'ccc' AND session = $session",
            params={"session": SESSIONS[2]},
        ).fetchone()

    assert close == (Decimal("1.5"),)


def test_a_build_that_is_not_daily_is_refused(tmp_path: Path) -> None:
    build = write_dataset(tmp_path, _spec())
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    manifest["interval"] = "1h"
    manifest["content_hash"] = content_hash_of(DatasetManifest.from_json(manifest))
    (build / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(UnsupportedManifestError, match="1h"):
        connect(LocalDatasetProvider(tmp_path), DATASET)


def test_history_rows_are_the_bars_outside_membership_from_where_history_starts(
    tmp_path: Path,
) -> None:
    # ccc is admitted on SESSIONS[3]; exported with history, its two bars
    # before that are in the file. In SQL they are the rows of bars that no
    # spell covers, and the history view says where they begin.
    spec = _spec()
    spec.bars += [SyntheticBar("ccc", session, Decimal(24_000)) for session in SESSIONS[1:3]]
    spec.history_sessions = 2
    write_dataset(tmp_path, spec)

    with connect(LocalDatasetProvider(tmp_path), DATASET) as connection:
        started = connection.sql("SELECT * FROM history ORDER BY instrument_id").fetchall()
        outside = connection.sql(
            "SELECT b.instrument_id, b.session FROM bars b "
            "WHERE NOT EXISTS (SELECT 1 FROM membership m WHERE m.instrument_id = b.instrument_id "
            "AND m.start <= b.session AND (m.until IS NULL OR b.session < m.until)) "
            "ORDER BY 1, 2"
        ).fetchall()
        sessions = connection.sql("SELECT history_sessions FROM manifest").fetchone()

    assert started == [("aaa", SESSIONS[0]), ("bbb", SESSIONS[1]), ("ccc", SESSIONS[1])]
    assert outside == [("ccc", SESSIONS[1]), ("ccc", SESSIONS[2])]
    assert sessions == (2,)


def test_without_history_the_history_view_is_empty(
    point_in_time: duckdb.DuckDBPyConnection,
) -> None:
    assert point_in_time.sql("SELECT count(*) FROM history").fetchone() == (0,)
