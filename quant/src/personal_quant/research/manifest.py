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

Schema 3 adds history (ADR-025): ``history_sessions``, each instrument's
``history_from`` and each spell's ``removal_announced_on``. They enter the hash
only from schema 3, so a schema 1 or 2 manifest hashes exactly as it was
written; read from one, they are zero and ``None``.

Schema 4 adds ``fundamentals`` (ADR-031): what ``fundamentals.parquet`` holds,
or null when the export carried none. It enters the hash from schema 4 only,
except its licence note, which no source's note does. A ``ratio_version`` this
reader does not know is refused: the ratios would be read under rules it
cannot name.
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
    "DatasetFundamentals",
    "DatasetInstrument",
    "DatasetManifest",
    "DatasetSpell",
    "Membership",
    "NotCollected",
    "UnsupportedManifestError",
    "content_hash_of",
    "dotnet_round_trip",
]

Membership = Literal["as_of", "point_in_time"]

SUPPORTED_SCHEMA_VERSIONS = frozenset({1, 2, 3, 4})

HISTORY_SCHEMA_VERSION = 3
"""The first schema whose manifests carry history and removal announcements."""

FUNDAMENTALS_SCHEMA_VERSION = 4
"""The first schema whose manifests carry the fundamentals object."""

SUPPORTED_RATIO_VERSIONS = frozenset({1, 2})
"""The ratio rules this reader can name: ADR-030 is 1, ADR-032 is 2."""

SESSION_CUT = "session_date_00utc"
"""The cut every fundamentals row is read at (ADR-031)."""

GAP_REASONS = frozenset({"ids_capture_gap", "out_of_scope", "not_imported"})
"""Why an exported instrument has no fundamentals."""

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
    at the window's end. ``announced_on`` is the admission's announcement and
    ``removal_announced_on`` the removal's, when it was published by the
    window's last day; trading either before it is look-ahead.
    """

    start: date
    until: date | None
    announced_on: date | None
    removal_announced_on: date | None = None

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

    ``history_from`` is the first session of the instrument's rows when the
    export carried history, and ``None`` otherwise. Rows from it up to the
    instrument's last membership session are data a feature may read — before
    its first spell, or between two — and never membership.
    """

    instrument_id: str
    ticker_at_as_of: str
    exchange: str
    spells: tuple[DatasetSpell, ...] | None
    history_from: date | None = None


@dataclass(frozen=True, slots=True)
class DatasetFile:
    """A data file of the build, with the digest it must still have."""

    name: str
    row_count: int
    size: int
    sha256: str


@dataclass(frozen=True, slots=True)
class NotCollected:
    """An exported instrument with no report held at all, and why.

    ``ids_capture_gap`` may be fixed; ``out_of_scope`` will not be;
    ``not_imported`` means nobody imported it, or not by the observation
    instant. It has no fundamentals rows, which is a different fact from a
    name whose ratios are all absent.
    """

    instrument_id: str
    reason: str


@dataclass(frozen=True, slots=True)
class DatasetFundamentals:
    """What ``fundamentals.parquet`` holds (schema 4, ADR-031)."""

    ratio_version: int
    source: str
    licence_note: str
    cut: str
    rows: int
    not_collected: tuple[NotCollected, ...]


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
    history_sessions: int = 0
    fundamentals: DatasetFundamentals | None = None

    @staticmethod
    def from_json(raw: Mapping[str, Any]) -> DatasetManifest:
        """Read a manifest from its parsed JSON.

        Args:
            raw: The decoded ``manifest.json``.

        Returns:
            The manifest.

        Raises:
            UnsupportedManifestError: The schema version, a policy or the ratio
                version is not one this reader knows. A reader that guessed
                which fields mean what would load a dataset it cannot describe.
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
        with_history = version >= HISTORY_SCHEMA_VERSION

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
            instruments=tuple(_instrument(item, with_history) for item in raw["instruments"]),
            interval=str(raw["interval"]),
            from_date=_required_date(raw["from_date"]),
            to_date=_required_date(raw["to_date"]),
            transformation_version=int(raw["transformation_version"]),
            validation_version=int(raw["validation_version"]),
            adjustment_version=int(raw["adjustment_version"]),
            sources=tuple(str(source["code"]) for source in raw["sources"]),
            files=tuple(_file(item) for item in raw["files"]),
            history_sessions=(
                _count(raw["history_sessions"], "history_sessions") if with_history else 0
            ),
            # Read only from schema 4, for the reason history is read only
            # from schema 3: an older hash does not cover it.
            fundamentals=(
                _fundamentals(raw["fundamentals"])
                if version >= FUNDAMENTALS_SCHEMA_VERSION
                else None
            ),
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

    # Schema 3 only, so earlier manifests hash exactly as they were written.
    with_history = manifest.schema_version >= HISTORY_SCHEMA_VERSION

    if with_history:
        canonical += f"{manifest.history_sessions}|"

    # Schema 4 only, likewise.
    if manifest.schema_version >= FUNDAMENTALS_SCHEMA_VERSION:
        canonical += _fundamentals_part(manifest.fundamentals)

    canonical += "".join(
        _instrument_part(instrument, with_history) for instrument in manifest.instruments
    )
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


def _instrument_part(instrument: DatasetInstrument, with_history: bool) -> str:
    rendered = f"{instrument.instrument_id},"

    if with_history:
        history_from = instrument.history_from
        rendered += f"{'none' if history_from is None else _format(history_from)},"

    for spell in instrument.spells or ():
        until = "open" if spell.until is None else _format(spell.until)
        announced = _announcement(spell.announced_on)
        rendered += f"{_format(spell.start)}~{until}~{announced}"

        if with_history:
            rendered += f"~{_announcement(spell.removal_announced_on)}"

        rendered += ";"

    return rendered


def _fundamentals_part(fundamentals: DatasetFundamentals | None) -> str:
    if fundamentals is None:
        return "fundamentals:none|"

    rendered = (
        f"fundamentals:{fundamentals.ratio_version},{fundamentals.source},"
        f"{fundamentals.cut},{fundamentals.rows},"
    )

    # Ordered by id, as the exporter renders them, whatever order the file holds.
    for gap in sorted(fundamentals.not_collected, key=lambda gap: gap.instrument_id):
        rendered += f"{gap.instrument_id}~{gap.reason};"

    return rendered + "|"


def _fundamentals(raw: Mapping[str, Any] | None) -> DatasetFundamentals | None:
    if raw is None:
        return None

    ratio_version = _count(raw["ratio_version"], "fundamentals.ratio_version")

    if ratio_version not in SUPPORTED_RATIO_VERSIONS:
        raise UnsupportedManifestError(
            f"Ratio version {ratio_version} is not one this reader knows "
            f"({sorted(SUPPORTED_RATIO_VERSIONS)}); the ratios were computed under rules "
            "it cannot name."
        )

    cut = str(raw["cut"])

    if cut != SESSION_CUT:
        raise UnsupportedManifestError(f"Fundamentals cut '{cut}' is not known.")

    gaps: list[NotCollected] = []

    for item in raw["not_collected"]:
        reason = str(item["reason"])

        if reason not in GAP_REASONS:
            raise UnsupportedManifestError(f"Not-collected reason '{reason}' is not known.")

        gaps.append(NotCollected(instrument_id=str(item["instrument_id"]), reason=reason))

    return DatasetFundamentals(
        ratio_version=ratio_version,
        source=str(raw["source"]),
        licence_note=str(raw["licence_note"]),
        cut=cut,
        rows=_count(raw["rows"], "fundamentals.rows"),
        not_collected=tuple(gaps),
    )


def _announcement(value: date | None) -> str:
    return "unannounced" if value is None else _format(value)


def _instrument(raw: Mapping[str, Any], with_history: bool) -> DatasetInstrument:
    spells: Sequence[Mapping[str, Any]] | None = raw.get("spells")

    # Fields a schema did not have are not read from it, even if present: the
    # hash of an older manifest does not cover them, so trusting one would
    # let an edit through unverified.
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
                removal_announced_on=(
                    _date(spell.get("removal_announced_on")) if with_history else None
                ),
            )
            for spell in spells
        ),
        history_from=_date(raw.get("history_from")) if with_history else None,
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


def _count(value: object, field: str) -> int:
    # bool is an int in Python; a manifest's counts are JSON integers or nothing.
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise UnsupportedManifestError(f"'{field}' must be a whole number, not {value!r}.")

    return value


def _date(value: object) -> date | None:
    return None if value is None else date.fromisoformat(str(value))


def _required_date(value: object) -> date:
    return date.fromisoformat(str(value))


def _format(value: date) -> str:
    return value.isoformat()
