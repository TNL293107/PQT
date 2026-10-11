"""Reading the manifest of a canonical dataset, and checking it.

A dataset is exported by the backend (``pqt dataset export``) as Parquet beside
a JSON manifest. The manifest says what the data is — universe, membership
mode, window, rule versions, sources — and carries two kinds of hash: each
file's SHA-256, and a ``content_hash`` over the manifest's own fields.

The content hash is recomputed here exactly as the .NET exporter computes it
(``DatasetIdentity.ContentHashOf``). That is the point of it: a manifest edited
by hand — a window widened, an as-of export relabelled point-in-time — no
longer matches, and a reader that recomputes it refuses the dataset instead of
trusting the edit. The rendering below follows the C# one detail for detail,
including .NET's spelling of booleans, enum names and round-trip instants.
"""

from __future__ import annotations

import hashlib
import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import UTC, date, datetime
from typing import Any, Literal

__all__ = [
    "DatasetFile",
    "DatasetInstrument",
    "DatasetManifest",
    "DatasetSpell",
    "Membership",
    "UnsupportedManifestError",
    "content_hash_of",
    "dotnet_round_trip",
]

Membership = Literal["as_of", "point_in_time"]

SUPPORTED_SCHEMA_VERSIONS = frozenset({1, 2})

# The manifest spells policies the way the API and CLI do; the hash uses the
# C# enum name, which is what StringBuilder.Append(enum) writes.
_POLICY_NAMES: Mapping[str, str] = {"strict": "Strict", "permissive": "Permissive"}

_INSTANT = re.compile(
    r"^(?P<stamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})"
    r"(?:\.(?P<fraction>\d{1,7}))?"
    r"(?P<offset>Z|[+-]\d{2}:\d{2})$"
)


class UnsupportedManifestError(ValueError):
    """Raised for a manifest this reader cannot interpret without guessing."""


@dataclass(frozen=True, slots=True)
class DatasetSpell:
    """A spell of membership, clipped to the dataset's window.

    ``until`` is exclusive and ``None`` when the instrument was still a member
    at the window's end. ``announced_on`` is the admission's announcement.
    """

    start: date
    until: date | None
    announced_on: date | None

    def includes(self, session: date) -> bool:
        """Report whether the instrument was a member on a session.

        Args:
            session: The session date.

        Returns:
            ``True`` when the half-open spell contains the date.
        """
        return self.start <= session and (self.until is None or session < self.until)


@dataclass(frozen=True, slots=True)
class DatasetInstrument:
    """One exported instrument, identified by its canonical id.

    The ticker is a label valid at the export's as-of date, not an identity:
    tickers change, and rows are keyed by ``instrument_id``.
    """

    instrument_id: str
    ticker_at_as_of: str
    exchange: str
    spells: tuple[DatasetSpell, ...] | None


@dataclass(frozen=True, slots=True)
class DatasetFile:
    """A data file of the build, with the digest it must still have."""

    name: str
    row_count: int
    size: int
    sha256: str


@dataclass(frozen=True, slots=True)
class DatasetManifest:
    """What a dataset build is, as its manifest states it."""

    dataset_id: str
    dataset_version: int
    schema_version: int
    content_hash: str
    known_as_of_utc: datetime | None
    known_as_of_text: str | None
    adjusted: bool
    announcement_policy: str
    universe_code: str
    universe_as_of: date | None
    membership: Membership
    instruments: tuple[DatasetInstrument, ...]
    interval: str
    from_date: date
    to_date: date
    transformation_version: int
    validation_version: int
    adjustment_version: int
    sources: tuple[str, ...]
    files: tuple[DatasetFile, ...]

    @staticmethod
    def from_json(raw: Mapping[str, Any]) -> DatasetManifest:
        """Read a manifest from its parsed JSON.

        Args:
            raw: The decoded ``manifest.json``.

        Returns:
            The manifest.

        Raises:
            UnsupportedManifestError: The schema version or a policy is not one
                this reader knows. A reader that guessed which fields mean what
                would load a dataset it cannot describe.
        """
        version = int(raw["schema_version"])

        if version not in SUPPORTED_SCHEMA_VERSIONS:
            raise UnsupportedManifestError(
                f"Manifest schema version {version} is not one this reader knows "
                f"({sorted(SUPPORTED_SCHEMA_VERSIONS)})."
            )

        policy = str(raw["announcement_policy"])

        if policy not in _POLICY_NAMES:
            raise UnsupportedManifestError(f"Announcement policy '{policy}' is not known.")

        # Version 1 predates the field, and every version 1 export was as-of.
        membership = str(raw.get("membership", "as_of"))

        if membership not in ("as_of", "point_in_time"):
            raise UnsupportedManifestError(f"Membership mode '{membership}' is not known.")

        known_as_of_text = raw.get("known_as_of_utc")

        return DatasetManifest(
            dataset_id=str(raw["dataset_id"]),
            dataset_version=int(raw["dataset_version"]),
            schema_version=version,
            content_hash=str(raw["content_hash"]),
            known_as_of_utc=_instant(known_as_of_text),
            known_as_of_text=None if known_as_of_text is None else str(known_as_of_text),
            adjusted=_boolean(raw["adjusted"], "adjusted"),
            announcement_policy=policy,
            universe_code=str(raw["universe_code"]),
            universe_as_of=_date(raw.get("universe_as_of")),
            membership="point_in_time" if membership == "point_in_time" else "as_of",
            instruments=tuple(_instrument(item) for item in raw["instruments"]),
            interval=str(raw["interval"]),
            from_date=_required_date(raw["from_date"]),
            to_date=_required_date(raw["to_date"]),
            transformation_version=int(raw["transformation_version"]),
            validation_version=int(raw["validation_version"]),
            adjustment_version=int(raw["adjustment_version"]),
            sources=tuple(str(source["code"]) for source in raw["sources"]),
            files=tuple(_file(item) for item in raw["files"]),
        )


def content_hash_of(manifest: DatasetManifest) -> str:
    """Compute a manifest's content hash the way the .NET exporter does.

    Covers every field except the dataset version, the creation instant and
    the commit — the three that say which run produced the build rather than
    what the data is.

    Args:
        manifest: The manifest; its recorded content hash is ignored.

    Returns:
        The lowercase hex SHA-256 digest.
    """
    known_as_of = (
        "current"
        if manifest.known_as_of_text is None
        else dotnet_round_trip(manifest.known_as_of_text)
    )

    parts: list[str] = [
        manifest.dataset_id,
        str(manifest.schema_version),
        known_as_of,
        "True" if manifest.adjusted else "False",
        _POLICY_NAMES[manifest.announcement_policy],
        manifest.universe_code,
        "point-in-time" if manifest.universe_as_of is None else _format(manifest.universe_as_of),
    ]

    canonical = "|".join(parts) + "|"

    # Appended only for point-in-time, so as-of and schema 1 manifests hash
    # exactly as they did before the field existed.
    if manifest.membership == "point_in_time":
        canonical += "point_in_time|"

    canonical += "|".join(
        [
            manifest.interval,
            _format(manifest.from_date),
            _format(manifest.to_date),
            str(manifest.transformation_version),
            str(manifest.validation_version),
            str(manifest.adjustment_version),
        ]
    )
    canonical += "|"
    canonical += "".join(_instrument_part(instrument) for instrument in manifest.instruments)
    canonical += "|"
    canonical += "".join(f"{source}," for source in manifest.sources)
    canonical += "|"
    canonical += "".join(f"{file.name}:{file.sha256}," for file in manifest.files)

    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def dotnet_round_trip(text: str) -> str:
    """Render an ISO instant the way .NET's ``"O"`` format writes it.

    System.Text.Json writes an instant with its fraction trimmed; ``"O"``
    always writes seven fractional digits and a numeric offset. The content
    hash is computed over the latter.

    Args:
        text: An ISO 8601 instant as found in a manifest.

    Returns:
        The same instant in .NET round-trip form.

    Raises:
        UnsupportedManifestError: The text is not an instant in that form.
    """
    match = _INSTANT.match(text)

    if match is None:
        raise UnsupportedManifestError(f"'{text}' is not an ISO 8601 instant.")

    fraction = (match["fraction"] or "").ljust(7, "0")
    offset = "+00:00" if match["offset"] == "Z" else match["offset"]

    return f"{match['stamp']}.{fraction}{offset}"


def _instrument_part(instrument: DatasetInstrument) -> str:
    rendered = f"{instrument.instrument_id},"

    for spell in instrument.spells or ():
        until = "open" if spell.until is None else _format(spell.until)
        announced = "unannounced" if spell.announced_on is None else _format(spell.announced_on)
        rendered += f"{_format(spell.start)}~{until}~{announced};"

    return rendered


def _instrument(raw: Mapping[str, Any]) -> DatasetInstrument:
    spells: Sequence[Mapping[str, Any]] | None = raw.get("spells")

    return DatasetInstrument(
        instrument_id=str(raw["instrument_id"]),
        ticker_at_as_of=str(raw["ticker_at_as_of"]),
        exchange=str(raw["exchange"]),
        spells=None
        if spells is None
        else tuple(
            DatasetSpell(
                start=_required_date(spell["from"]),
                until=_date(spell.get("until")),
                announced_on=_date(spell.get("announced_on")),
            )
            for spell in spells
        ),
    )


def _file(raw: Mapping[str, Any]) -> DatasetFile:
    return DatasetFile(
        name=str(raw["name"]),
        row_count=int(raw["row_count"]),
        size=int(raw["bytes"]),
        sha256=str(raw["sha256"]),
    )


def _instant(value: object) -> datetime | None:
    if value is None:
        return None

    rendered = dotnet_round_trip(str(value))

    # datetime takes six fractional digits; the seventh is a tenth of a
    # microsecond and is kept only in the rendering the hash uses.
    return datetime.fromisoformat(rendered[:26] + rendered[27:]).astimezone(UTC)


def _boolean(value: object, field: str) -> bool:
    # bool("false") is True; a manifest's booleans are JSON booleans or nothing.
    if not isinstance(value, bool):
        raise UnsupportedManifestError(f"'{field}' must be true or false, not {value!r}.")

    return value


def _date(value: object) -> date | None:
    return None if value is None else date.fromisoformat(str(value))


def _required_date(value: object) -> date:
    return date.fromisoformat(str(value))


def _format(value: date) -> str:
    return value.isoformat()
