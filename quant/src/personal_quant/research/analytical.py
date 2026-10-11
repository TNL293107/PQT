"""SQL over a verified dataset build, through embedded DuckDB (U7).

U7's analytical tier is DuckDB, in process: a researcher asks a question of a
build in SQL, with no server to run. DuckDB's own way in is a path —
``read_parquet('…/bars.parquet')`` — and a path is exactly the gap U6 closed.
A file read by path after it was verified is not the file that was verified;
it can have been replaced in between, and a result computed from it is then
tied to a build it was never computed from.

So :func:`connect` never gives DuckDB a path. It verifies the build through
:meth:`~personal_quant.research.datasets.LocalDatasetProvider.verify`, hands
DuckDB the bytes that were hashed, as Arrow tables held in memory, and opens
the connection with DuckDB's file system access switched off and its
configuration locked. Nothing on the connection can read a file — not the
build, not another — so every row a query sees is a verified one.

Three views:

* ``bars`` — one row per instrument and session. ``session`` is the UTC date
  of ``opened_at_utc``, as the Python loader takes it. Prices are cast from the
  exporter's strings to ``DECIMAL(38,10)``, never through ``DOUBLE``; DuckDB
  would round a string with more than ten decimals without a word, so a build
  holding one is refused instead. The exporter writes at most six, so the
  refusal guards a future change, not today's builds.
* ``membership`` — the spells of a point-in-time build, half-open as the
  manifest states them; an as-of build has one open spell per instrument from
  the window's start, since it holds one constituent set throughout.
* ``manifest`` — one row naming the build, so every query can be tied to the
  dataset version and content hash it was computed from.
"""

from __future__ import annotations

from collections.abc import Iterable
from datetime import UTC
from decimal import Context, Decimal
from typing import Any

import duckdb
import pyarrow as pa

from personal_quant.research.datasets import (
    BARS_FILE,
    LocalDatasetProvider,
    VerifiedBuild,
    read_bars_table,
    require_daily,
)
from personal_quant.research.manifest import DatasetSpell
from personal_quant.research.protocols import Bar

__all__ = ["PRICE_TYPE", "UnrepresentablePriceError", "connect"]

PRICE_PRECISION = 38
PRICE_SCALE = 10
PRICE_TYPE = f"DECIMAL({PRICE_PRECISION},{PRICE_SCALE})"
"""The SQL type every price is held in."""

_STEP = Decimal(1).scaleb(-PRICE_SCALE)
_LIMIT = Decimal(10) ** (PRICE_PRECISION - PRICE_SCALE)
# Wide enough that quantizing a price can never round it: the comparison
# after it is then a test of exactness, not of the context's precision.
_EXACT = Context(prec=2 * PRICE_PRECISION)

_PRICES = ("open", "high", "low", "close")

# The registered tables are the verified data as Arrow holds it; the views
# are what a query is meant to read. The SQL is spelled out rather than
# assembled, so the SQL that runs is the SQL read here; a test holds the
# price columns to PRICE_TYPE.
_BARS = "verified_bars"
_MEMBERSHIP = "verified_membership"
_MANIFEST = "verified_manifest"

_VIEWS = (
    """
    CREATE VIEW bars AS
    SELECT
        instrument_id,
        session,
        CAST(open AS DECIMAL(38,10)) AS open,
        CAST(high AS DECIMAL(38,10)) AS high,
        CAST(low AS DECIMAL(38,10)) AS low,
        CAST(close AS DECIMAL(38,10)) AS close,
        volume
    FROM verified_bars
    """,
    """
    CREATE VIEW membership AS
    SELECT instrument_id, "start", "until", announced_on
    FROM verified_membership
    """,
    """
    CREATE VIEW manifest AS
    SELECT *
    FROM verified_manifest
    """,
)


class UnrepresentablePriceError(ValueError):
    """Raised for a price ``DECIMAL(38,10)`` would round or cannot hold."""


def connect(
    provider: LocalDatasetProvider, dataset_id: str, version: int | None = None
) -> duckdb.DuckDBPyConnection:
    """Verify a build and open an in-memory DuckDB connection over it.

    The connection holds the ``bars``, ``membership`` and ``manifest`` views
    and cannot read or write a file; its configuration is locked, so that
    cannot be undone with ``SET``. Results leave through Python — ``fetchall``,
    ``arrow``, ``df`` — not through ``COPY``. Close it when done.

    Args:
        provider: Where the build is held.
        dataset_id: The dataset id.
        version: The build to open; the latest when ``None``.

    Returns:
        The connection.

    Raises:
        FileNotFoundError: No such build is held.
        DatasetIntegrityError: A file or the manifest does not match.
        UnsupportedManifestError: The build is not daily.
        UnrepresentablePriceError: A price would not survive the cast exactly.
    """
    build = provider.verify(dataset_id, version)

    require_daily(build.manifest)
    _require_representable(build.bars)

    # File system access is off from the first statement, and the setting
    # cannot be turned back on: registered Arrow tables are the only data the
    # connection can reach.
    connection = duckdb.connect(
        ":memory:", config={"enable_external_access": False, "lock_configuration": True}
    )

    try:
        connection.register(_BARS, read_bars_table(build.files[BARS_FILE]))
        connection.register(_MEMBERSHIP, _membership_table(build))
        connection.register(_MANIFEST, _manifest_table(build))

        for view in _VIEWS:
            connection.execute(view)
    except BaseException:
        connection.close()
        raise

    return connection


def _require_representable(bars: Iterable[Bar]) -> None:
    for bar in bars:
        for name in _PRICES:
            price: Decimal = getattr(bar, name)

            if not _representable(price):
                raise UnrepresentablePriceError(
                    f"{bar.instrument_id} {name} on {bar.session} is {price}, which "
                    f"{PRICE_TYPE} cannot hold exactly."
                )


def _representable(price: Decimal) -> bool:
    return (
        price.is_finite() and abs(price) < _LIMIT and price.quantize(_STEP, context=_EXACT) == price
    )


def _membership_table(build: VerifiedBuild) -> Any:  # noqa: ANN401 - pyarrow ships no types.
    manifest = build.manifest

    if manifest.membership == "as_of":
        whole_window = DatasetSpell(start=manifest.from_date, until=None, announced_on=None)
        spells = [(instrument.instrument_id, whole_window) for instrument in manifest.instruments]
    else:
        spells = [
            (instrument.instrument_id, spell)
            for instrument in manifest.instruments
            for spell in instrument.spells or ()
        ]

    return pa.table(
        {
            "instrument_id": pa.array([held for held, _ in spells], type=pa.string()),
            "start": pa.array([spell.start for _, spell in spells], type=pa.date32()),
            "until": pa.array([spell.until for _, spell in spells], type=pa.date32()),
            "announced_on": pa.array([spell.announced_on for _, spell in spells], type=pa.date32()),
        }
    )


def _manifest_table(build: VerifiedBuild) -> Any:  # noqa: ANN401 - pyarrow ships no types.
    manifest = build.manifest
    known_as_of = (
        None
        if manifest.known_as_of_utc is None
        # A zoned timestamp would need pytz to come back into Python; the
        # column's name carries the zone instead.
        else manifest.known_as_of_utc.astimezone(UTC).replace(tzinfo=None)
    )

    return pa.table(
        {
            "dataset_id": pa.array([manifest.dataset_id], type=pa.string()),
            "dataset_version": pa.array([manifest.dataset_version], type=pa.int32()),
            "content_hash": pa.array([manifest.content_hash], type=pa.string()),
            "universe_code": pa.array([manifest.universe_code], type=pa.string()),
            "membership": pa.array([manifest.membership], type=pa.string()),
            "adjusted": pa.array([manifest.adjusted], type=pa.bool_()),
            "announcement_policy": pa.array([manifest.announcement_policy], type=pa.string()),
            "known_as_of_utc": pa.array([known_as_of], type=pa.timestamp("us")),
            "from_date": pa.array([manifest.from_date], type=pa.date32()),
            "to_date": pa.array([manifest.to_date], type=pa.date32()),
        }
    )
