"""Locating and loading canonical datasets, verified before they are trusted.

A build lives at ``<root>/<dataset_id>/v<version>/`` beside its
``manifest.json``, as ``pqt dataset export`` writes it. Loading checks, in
order: that each file named by the manifest is a plain name inside the build,
that its size and SHA-256 are the recorded ones, and that the manifest still
hashes to its own ``content_hash``. The bytes that are hashed are the bytes
that are parsed; the file is not opened twice. The rows are then held to the
manifest: no duplicate session, no instrument it does not list, no session
outside its window, and — for a point-in-time build — no row outside the
instrument's spells, which the exporter never writes. Any mismatch is a hard
error. A result computed from a silently changed input is not reproducible, and
the point of a dataset version is that it is.

:meth:`LocalDatasetProvider.verify` does all of that once and hands back what it
checked, as a :class:`VerifiedBuild`. Every reader is built on it —
:meth:`LocalDatasetProvider.open` for Python,
:func:`personal_quant.research.analytical.connect` for DuckDB — so none of them
can skip a check, and none reads a file a second time.

Prices are read from the strings the exporter writes into
:class:`~decimal.Decimal`; nothing passes through a binary float.
"""

from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import date
from decimal import Decimal
from pathlib import Path
from types import MappingProxyType
from typing import Any

import pyarrow as pa
import pyarrow.parquet as pq

from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
)
from personal_quant.research.protocols import Bar

__all__ = [
    "CanonicalDataset",
    "DatasetIntegrityError",
    "LocalDatasetProvider",
    "VerifiedBuild",
    "read_bars_table",
    "require_daily",
]

MANIFEST_FILE = "manifest.json"
BARS_FILE = "bars.parquet"

_BUILD = re.compile(r"v(?P<version>[1-9][0-9]*)")
_COLUMNS = ["instrument_id", "opened_at_utc", "open", "high", "low", "close", "volume"]


class DatasetIntegrityError(RuntimeError):
    """Raised when a build does not match the manifest that describes it."""


@dataclass(frozen=True, slots=True)
class VerifiedBuild:
    """A build that passed every check, held as the bytes that were checked.

    Every reader of a build starts here — the Python loader and the DuckDB
    view alike — so none repeats the verification, and none goes back to the
    disk after it: a file replaced once it has been hashed is never the one
    that is read.

    Attributes:
        manifest: The manifest, its content hash recomputed and matched.
        files: Each data file the manifest names, by name, as it was hashed.
        bars: The rows of ``bars.parquet``, already held to the manifest.
    """

    manifest: DatasetManifest
    files: Mapping[str, bytes]
    bars: tuple[Bar, ...]


def require_daily(manifest: DatasetManifest) -> None:
    """Refuse a build whose bars are not one per session.

    Args:
        manifest: The build's manifest.

    Raises:
        UnsupportedManifestError: The build is not daily. Sessions here are
            dates, and an intraday build would be read as one bar per date.
    """
    if manifest.interval != "1d":
        raise UnsupportedManifestError(
            f"Only daily datasets can be read as sessions; this one is '{manifest.interval}'."
        )


class CanonicalDataset:
    """A loaded, verified dataset build."""

    def __init__(self, manifest: DatasetManifest, bars: Sequence[Bar]) -> None:
        """Index a build's bars by instrument and session.

        Args:
            manifest: The verified manifest.
            bars: Every bar the build holds.

        Raises:
            UnsupportedManifestError: The build is not daily. Sessions here are
                dates, and an intraday build would be read as one bar per date.
        """
        require_daily(manifest)

        by_instrument: dict[str, dict[date, Bar]] = {}

        for bar in bars:
            by_instrument.setdefault(bar.instrument_id, {})[bar.session] = bar

        self._manifest = manifest
        self._bars = {key: MappingProxyType(value) for key, value in by_instrument.items()}
        self._sessions = tuple(sorted({bar.session for bar in bars}))
        self._all = frozenset(instrument.instrument_id for instrument in manifest.instruments)

    @property
    def manifest(self) -> DatasetManifest:
        """The manifest the build was verified against."""
        return self._manifest

    @property
    def sessions(self) -> Sequence[date]:
        """Every session with at least one bar, oldest first."""
        return self._sessions

    def bars_of(self, instrument_id: str) -> Mapping[date, Bar]:
        """Return one instrument's bars by session; empty for an unknown one.

        Args:
            instrument_id: The canonical instrument id.

        Returns:
            A read-only mapping from session to bar.
        """
        return self._bars.get(instrument_id, MappingProxyType({}))

    def members_on(self, session: date) -> frozenset[str]:
        """Return the instruments that belonged to the universe on a session.

        For a point-in-time build, the instruments whose spell includes the
        session. For an as-of build, every instrument: it holds one
        constituent set for its whole window.

        Args:
            session: The session date.

        Returns:
            The members' instrument ids.
        """
        if self._manifest.membership == "as_of":
            return self._all

        return frozenset(
            instrument.instrument_id
            for instrument in self._manifest.instruments
            if any(spell.includes(session) for spell in instrument.spells or ())
        )


class LocalDatasetProvider:
    """Finds dataset builds in a directory the backend exports into."""

    def __init__(self, root: Path) -> None:
        """Point the provider at an export directory.

        Args:
            root: The directory holding one folder per dataset id.
        """
        self._root = root

    def versions(self, dataset_id: str) -> Sequence[int]:
        """Return the builds held for a dataset, oldest first.

        Args:
            dataset_id: The dataset id.

        Returns:
            The build versions; empty when none is held.
        """
        directory = self._root / _plain(dataset_id)

        if not directory.is_dir():
            return ()

        return tuple(
            sorted(
                int(match["version"])
                for entry in directory.iterdir()
                if entry.is_dir() and (match := _BUILD.fullmatch(entry.name)) is not None
            )
        )

    def open(self, dataset_id: str, version: int | None = None) -> CanonicalDataset:
        """Load and verify a build.

        Args:
            dataset_id: The dataset id.
            version: The build to open; the latest when ``None``.

        Returns:
            The verified dataset.

        Raises:
            FileNotFoundError: No such build is held.
            DatasetIntegrityError: A file or the manifest does not match.
        """
        build = self.verify(dataset_id, version)

        return CanonicalDataset(build.manifest, build.bars)

    def verify(self, dataset_id: str, version: int | None = None) -> VerifiedBuild:
        """Check a build against its manifest, and keep what was checked.

        Args:
            dataset_id: The dataset id.
            version: The build to verify; the latest when ``None``.

        Returns:
            The manifest, the verified bytes of each file, and the rows.

        Raises:
            FileNotFoundError: No such build is held.
            DatasetIntegrityError: A file or the manifest does not match.
        """
        held = self.versions(dataset_id)
        chosen = version if version is not None else (held[-1] if held else None)

        if chosen is None or chosen not in held:
            raise FileNotFoundError(
                f"No build {'v' + str(version) if version else ''} of dataset {dataset_id} "
                f"is held under {self._root}."
            )

        build = self._root / _plain(dataset_id) / f"v{chosen}"
        manifest = DatasetManifest.from_json(
            json.loads((build / MANIFEST_FILE).read_text(encoding="utf-8"))
        )

        if manifest.dataset_id != dataset_id or manifest.dataset_version != chosen:
            raise DatasetIntegrityError(
                f"{build} holds the manifest of {manifest.dataset_id} "
                f"v{manifest.dataset_version}, not of {dataset_id} v{chosen}."
            )

        payloads = _verify_files(build, manifest)
        _verify_content(manifest)

        bars = _read_bars(payloads[BARS_FILE])
        _verify_rows(manifest, bars)

        return VerifiedBuild(manifest, MappingProxyType(payloads), tuple(bars))


def _verify_files(build: Path, manifest: DatasetManifest) -> dict[str, bytes]:
    for file in manifest.files:
        if Path(file.name).name != file.name or file.name in ("", ".", ".."):
            raise DatasetIntegrityError(
                f"'{file.name}' is not a file name inside the build directory."
            )

    if BARS_FILE not in {file.name for file in manifest.files}:
        raise DatasetIntegrityError(f"The manifest of {build} names no {BARS_FILE}.")

    payloads: dict[str, bytes] = {}

    for file in manifest.files:
        path = build / file.name

        if not path.is_file():
            raise DatasetIntegrityError(f"{file.name} is named by the manifest but missing.")

        # Read once: the bytes verified here are the bytes parsed later, so a
        # file replaced between the two cannot slip through.
        payload = path.read_bytes()

        if len(payload) != file.size:
            raise DatasetIntegrityError(
                f"{file.name} is {len(payload)} bytes; the manifest records {file.size}."
            )

        digest = hashlib.sha256(payload).hexdigest()

        if digest != file.sha256:
            raise DatasetIntegrityError(
                f"{file.name} hashes to {digest} but the manifest records {file.sha256}."
            )

        payloads[file.name] = payload

    return payloads


def _verify_content(manifest: DatasetManifest) -> None:
    content = content_hash_of(manifest)

    if content != manifest.content_hash:
        raise DatasetIntegrityError(
            f"The manifest's content hash is {content} but it records "
            f"{manifest.content_hash}; its own fields have changed since it was written."
        )


def _verify_rows(manifest: DatasetManifest, bars: Sequence[Bar]) -> None:
    recorded = next(file.row_count for file in manifest.files if file.name == BARS_FILE)

    if len(bars) != recorded:
        raise DatasetIntegrityError(
            f"{BARS_FILE} holds {len(bars)} rows; the manifest records {recorded}."
        )

    listed = {instrument.instrument_id: instrument for instrument in manifest.instruments}
    point_in_time = manifest.membership == "point_in_time"

    if point_in_time and any(instrument.spells is None for instrument in listed.values()):
        raise DatasetIntegrityError("A point-in-time manifest lists an instrument with no spells.")

    seen: set[tuple[str, date]] = set()

    for bar in bars:
        key = (bar.instrument_id, bar.session)
        instrument = listed.get(bar.instrument_id)

        if instrument is None:
            raise DatasetIntegrityError(
                f"A row names {bar.instrument_id}, which the manifest does not list."
            )

        if key in seen:
            raise DatasetIntegrityError(f"{bar.instrument_id} has two rows for {bar.session}.")

        if not manifest.from_date <= bar.session <= manifest.to_date:
            raise DatasetIntegrityError(
                f"{bar.instrument_id} has a row for {bar.session}, outside the window "
                f"{manifest.from_date} to {manifest.to_date}."
            )

        if point_in_time and not any(
            spell.includes(bar.session) for spell in instrument.spells or ()
        ):
            raise DatasetIntegrityError(
                f"{bar.instrument_id} has a row for {bar.session}, outside its membership."
            )

        seen.add(key)


def read_bars_table(payload: bytes) -> Any:  # noqa: ANN401 - pyarrow ships no types.
    """Read ``bars.parquet`` into Arrow, one session date per row.

    The one place a session is derived from ``opened_at_utc``, so every
    reader of a build — Python or DuckDB — agrees on it. The date is taken in
    Arrow, as the UTC date of the instant: no Python time zone object is
    built, and no database session's time zone is consulted.

    Args:
        payload: The verified bytes of ``bars.parquet``.

    Returns:
        A ``pyarrow.Table`` of ``instrument_id``, ``session`` (``date32``),
        ``open``, ``high``, ``low`` and ``close`` as the exporter's strings,
        and ``volume``.
    """
    table = pq.read_table(pa.BufferReader(payload), columns=_COLUMNS)
    opened = table.column("opened_at_utc")

    if opened.type.tz is not None:
        # A zoned instant is stored as UTC; dropping the zone keeps the UTC
        # wall time, whose date is the session.
        opened = opened.cast(pa.timestamp(opened.type.unit))

    return table.set_column(_COLUMNS.index("opened_at_utc"), "session", opened.cast(pa.date32()))


def _read_bars(payload: bytes) -> list[Bar]:
    columns = read_bars_table(payload).to_pydict()
    names = ["instrument_id", "session", "open", "high", "low", "close", "volume"]

    return [
        Bar(
            instrument_id=str(instrument),
            session=session,
            open=Decimal(open_),
            high=Decimal(high),
            low=Decimal(low),
            close=Decimal(close),
            volume=int(volume),
        )
        for instrument, session, open_, high, low, close, volume in zip(
            *(columns[name] for name in names), strict=True
        )
    ]


def _plain(dataset_id: str) -> str:
    if not dataset_id or Path(dataset_id).name != dataset_id or dataset_id in (".", ".."):
        raise ValueError(f"'{dataset_id}' is not a dataset id: it must be a plain name.")

    return dataset_id
