"""Synthetic canonical datasets, written the way the exporter writes them.

Real exports are not committed — their sources grant no licence to
redistribute — so tests build their own: a ``bars.parquet`` with the exporter's
column layout and a manifest whose file digest and content hash are computed
exactly as the backend computes them.
"""

from __future__ import annotations

import hashlib
import json
from collections.abc import Mapping, Sequence
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta
from decimal import Decimal
from pathlib import Path
from typing import Any

import pyarrow as pa
import pyarrow.parquet as pq

from personal_quant.research.manifest import DatasetManifest, content_hash_of

_SATURDAY = 5


@dataclass(frozen=True)
class SyntheticBar:
    """A bar to write; the other OHLC prices are set to the close."""

    instrument_id: str
    session: date
    close: Decimal
    volume: int = 1_000


@dataclass
class SyntheticDataset:
    """Everything a test needs to write one dataset build."""

    dataset_id: str = "0123456789abcdef"
    version: int = 1
    bars: list[SyntheticBar] = field(default_factory=list)
    spells: Mapping[str, Sequence[tuple[date, date | None]]] | None = None
    instruments: Sequence[str] = ()
    adjusted: bool = True
    # The exporter's timestamps carry no zone; a reader must cope if one does.
    opened_at_zone: str | None = None
    # History, as a schema 3 export with --history writes it. With a count
    # above zero each instrument's history_from is its first row unless
    # given here; the window is the bars' first and last session unless given.
    history_sessions: int = 0
    history_from: Mapping[str, date | None] | None = None
    window: tuple[date, date] | None = None


def weekdays(start: date, count: int) -> list[date]:
    """Return ``count`` weekdays from ``start`` onwards, as stand-in sessions."""
    days: list[date] = []
    day = start

    while len(days) < count:
        if day.weekday() < _SATURDAY:
            days.append(day)

        day += timedelta(days=1)

    return days


def write_dataset(root: Path, spec: SyntheticDataset) -> Path:
    """Write a dataset build under ``root`` and return its directory."""
    build = root / spec.dataset_id / f"v{spec.version}"
    build.mkdir(parents=True)

    ordered = sorted(spec.bars, key=lambda bar: (bar.instrument_id, bar.session))
    table = pa.table(
        {
            "instrument_id": [bar.instrument_id for bar in ordered],
            "opened_at_utc": pa.array(
                [datetime(bar.session.year, bar.session.month, bar.session.day) for bar in ordered],
                type=pa.timestamp("ns", tz=spec.opened_at_zone),
            ),
            "open": [str(bar.close) for bar in ordered],
            "high": [str(bar.close) for bar in ordered],
            "low": [str(bar.close) for bar in ordered],
            "close": [str(bar.close) for bar in ordered],
            "volume": pa.array([bar.volume for bar in ordered], type=pa.int64()),
            "turnover": pa.array([None] * len(ordered), type=pa.string()),
            "source": ["TEST"] * len(ordered),
            "revision": pa.array([0] * len(ordered), type=pa.int32()),
            "price_factor": ["1"] * len(ordered),
            "share_factor": ["1"] * len(ordered),
        }
    )
    pq.write_table(table, build / "bars.parquet")

    payload = (build / "bars.parquet").read_bytes()
    instruments = list(spec.instruments) or sorted({bar.instrument_id for bar in spec.bars})
    sessions = [bar.session for bar in spec.bars]
    from_date, to_date = spec.window or (min(sessions), max(sessions))
    first_rows = {
        instrument: min(
            (bar.session for bar in spec.bars if bar.instrument_id == instrument), default=None
        )
        for instrument in instruments
    }

    raw: dict[str, Any] = {
        "dataset_id": spec.dataset_id,
        "dataset_version": spec.version,
        "schema_version": 3,
        "content_hash": "",
        "known_as_of_utc": None,
        "adjusted": spec.adjusted,
        "announcement_policy": "strict",
        "universe_code": "TEST30",
        "universe_as_of": None if spec.spells is not None else from_date.isoformat(),
        "membership": "point_in_time" if spec.spells is not None else "as_of",
        "instruments": [
            {
                "instrument_id": instrument,
                "ticker_at_as_of": instrument.upper()[:3],
                "exchange": "HOSE",
                "spells": None
                if spec.spells is None
                else [
                    {
                        "from": start.isoformat(),
                        "until": None if until is None else until.isoformat(),
                        "announced_on": None,
                        "removal_announced_on": None,
                    }
                    for start, until in spec.spells.get(instrument, ())
                ],
                "history_from": _history_from(spec, instrument, first_rows[instrument]),
            }
            for instrument in instruments
        ],
        "interval": "1d",
        "from_date": from_date.isoformat(),
        "to_date": to_date.isoformat(),
        "transformation_version": 1,
        "validation_version": 1,
        "adjustment_version": 2,
        "sources": [{"code": "TEST", "licence_note": "Synthetic."}],
        "files": [
            {
                "name": "bars.parquet",
                "row_count": len(ordered),
                "bytes": len(payload),
                "sha256": hashlib.sha256(payload).hexdigest(),
            }
        ],
        "history_sessions": spec.history_sessions,
        "row_count": len(ordered),
        "created_at_utc": "2026-10-11T00:00:00+00:00",
        "created_by_commit": None,
    }
    raw["content_hash"] = content_hash_of(DatasetManifest.from_json(raw))

    (build / "manifest.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")

    return build


def _history_from(spec: SyntheticDataset, instrument: str, first_row: date | None) -> str | None:
    if spec.history_from is not None and instrument in spec.history_from:
        given = spec.history_from[instrument]
        return None if given is None else given.isoformat()

    if spec.history_sessions == 0 or first_row is None:
        return None

    return first_row.isoformat()
