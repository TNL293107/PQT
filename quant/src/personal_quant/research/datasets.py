"""Locating and loading canonical datasets, verified before they are trusted.

A build lives at ``<root>/<dataset_id>/v<version>/`` beside its
``manifest.json``, as ``pqt dataset export`` writes it. Loading checks, in
order: that each file named by the manifest is a plain name inside the build,
that its size and SHA-256 are the recorded ones, and that the manifest still
hashes to its own ``content_hash``. Any mismatch is a hard error. A result
computed from a silently changed input is not reproducible, and the point of a
dataset version is that it is.

Prices are read from the strings the exporter writes into
:class:`~decimal.Decimal`; nothing passes through a binary float.
"""

from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Mapping, Sequence
from datetime import date
from decimal import Decimal
from pathlib import Path
from types import MappingProxyType

import pyarrow.parquet as pq

from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
)
from personal_quant.research.protocols import Bar

__all__ = ["CanonicalDataset", "DatasetIntegrityError", "LocalDatasetProvider"]

MANIFEST_FILE = "manifest.json"
BARS_FILE = "bars.parquet"

_BUILD = re.compile(r"^v(?P<version>[1-9]\d*)$")
_CHUNK = 1 << 20
_COLUMNS = ["instrument_id", "opened_at_utc", "open", "high", "low", "close", "volume"]


class DatasetIntegrityError(RuntimeError):
    """Raised when a build does not match the manifest that describes it."""


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
        if manifest.interval != "1d":
            raise UnsupportedManifestError(
                f"Only daily datasets can be read as sessions; this one is '{manifest.interval}'."
            )

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
                if entry.is_dir() and (match := _BUILD.match(entry.name)) is not None
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

        _verify_files(build, manifest)
        _verify_content(manifest)

        return CanonicalDataset(manifest, _read_bars(build / BARS_FILE))


def _verify_files(build: Path, manifest: DatasetManifest) -> None:
    for file in manifest.files:
        if Path(file.name).name != file.name or file.name in ("", ".", ".."):
            raise DatasetIntegrityError(
                f"'{file.name}' is not a file name inside the build directory."
            )

    if BARS_FILE not in {file.name for file in manifest.files}:
        raise DatasetIntegrityError(f"The manifest of {build} names no {BARS_FILE}.")

    for file in manifest.files:
        path = build / file.name

        if not path.is_file():
            raise DatasetIntegrityError(f"{file.name} is named by the manifest but missing.")

        if path.stat().st_size != file.size:
            raise DatasetIntegrityError(
                f"{file.name} is {path.stat().st_size} bytes; the manifest records {file.size}."
            )

        digest = _sha256(path)

        if digest != file.sha256:
            raise DatasetIntegrityError(
                f"{file.name} hashes to {digest} but the manifest records {file.sha256}."
            )


def _verify_content(manifest: DatasetManifest) -> None:
    content = content_hash_of(manifest)

    if content != manifest.content_hash:
        raise DatasetIntegrityError(
            f"The manifest's content hash is {content} but it records "
            f"{manifest.content_hash}; its own fields have changed since it was written."
        )


def _read_bars(path: Path) -> list[Bar]:
    columns = pq.read_table(path, columns=_COLUMNS).to_pydict()

    return [
        Bar(
            instrument_id=str(instrument),
            session=opened.date(),
            open=Decimal(open_),
            high=Decimal(high),
            low=Decimal(low),
            close=Decimal(close),
            volume=int(volume),
        )
        for instrument, opened, open_, high, low, close, volume in zip(
            *(columns[name] for name in _COLUMNS), strict=True
        )
    ]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()

    with path.open("rb") as stream:
        while chunk := stream.read(_CHUNK):
            digest.update(chunk)

    return digest.hexdigest()


def _plain(dataset_id: str) -> str:
    if not dataset_id or Path(dataset_id).name != dataset_id or dataset_id in (".", ".."):
        raise ValueError(f"'{dataset_id}' is not a dataset id: it must be a plain name.")

    return dataset_id
