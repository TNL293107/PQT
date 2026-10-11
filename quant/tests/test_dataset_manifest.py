"""The manifest reader agrees with the .NET exporter that wrote it.

The schema 1 and 2 fixtures are manifests ``pqt dataset export`` actually
produced; the schema 3 one is written by the .NET unit test
``DatasetManifestGoldenTests`` through the exporter's own serialiser and
``ContentHashOf``, and that test fails if the committed file drifts from what
.NET would write. None carries prices. If Python computed a content hash that
differed from the one .NET recorded, every real dataset would fail to load — or
worse, a reader that skipped the check would load one whose manifest had been
edited.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from datetime import UTC, date, datetime
from pathlib import Path
from typing import Any

import pytest

from personal_quant.research.manifest import (
    DatasetManifest,
    UnsupportedManifestError,
    content_hash_of,
    dotnet_round_trip,
)

FIXTURES = Path(__file__).parent / "fixtures" / "manifests"


def _raw(name: str) -> dict[str, Any]:
    raw: dict[str, Any] = json.loads((FIXTURES / name).read_text(encoding="utf-8"))
    return raw


@pytest.mark.parametrize(
    "name",
    [
        "v1-as-of.json",
        "v2-point-in-time.json",
        # Exported with --known-as-of across the August 2026 review: a non-null
        # instant, unannounced spells and closed ones, as .NET rendered them.
        "v2-known-as-of.json",
        # Schema 3 with history: a non-null history_from and a null one, an
        # announced removal and an unannounced one, as .NET rendered them.
        "v3-history.json",
    ],
)
def test_the_content_hash_matches_the_one_dotnet_recorded(name: str) -> None:
    manifest = DatasetManifest.from_json(_raw(name))

    assert content_hash_of(manifest) == manifest.content_hash


def test_a_schema_1_manifest_reads_as_an_as_of_export() -> None:
    # Version 1 predates the membership field; every v1 export was as-of.
    manifest = DatasetManifest.from_json(_raw("v1-as-of.json"))

    assert manifest.membership == "as_of"
    assert manifest.universe_as_of == date(2026, 8, 3)


def test_a_point_in_time_manifest_carries_each_instruments_spells() -> None:
    manifest = DatasetManifest.from_json(_raw("v2-point-in-time.json"))

    assert manifest.membership == "point_in_time"
    assert all(instrument.spells is not None for instrument in manifest.instruments)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("adjusted", False),
        ("announcement_policy", "permissive"),
        ("to_date", "2026-09-16"),
        ("membership", "as_of"),
    ],
)
def test_an_edited_manifest_no_longer_matches_its_hash(field: str, value: object) -> None:
    # Flipping the membership mode by hand must not verify either: a consumer
    # reads that field to decide which rows it may trust.
    raw = _raw("v2-point-in-time.json")
    raw[field] = value

    manifest = DatasetManifest.from_json(raw)

    assert content_hash_of(manifest) != manifest.content_hash


def test_an_edited_spell_no_longer_matches_its_hash() -> None:
    raw = _raw("v2-point-in-time.json")
    raw["instruments"][0]["spells"][0]["from"] = "2026-07-02"

    manifest = DatasetManifest.from_json(raw)

    assert content_hash_of(manifest) != manifest.content_hash


def test_an_unknown_schema_version_is_refused_rather_than_guessed() -> None:
    raw = _raw("v3-history.json")
    raw["schema_version"] = 4

    with pytest.raises(UnsupportedManifestError, match="schema version 4"):
        DatasetManifest.from_json(raw)


def test_a_schema_3_manifest_carries_its_history_and_removal_announcements() -> None:
    manifest = DatasetManifest.from_json(_raw("v3-history.json"))
    by_ticker = {instrument.ticker_at_as_of: instrument for instrument in manifest.instruments}

    assert manifest.history_sessions == 253
    assert by_ticker["AAA"].history_from == date(2025, 6, 25)
    assert by_ticker["EEE"].history_from is None
    assert by_ticker["CCC"].spells is not None
    assert by_ticker["CCC"].spells[0].removal_announced_on == date(2026, 7, 15)
    assert by_ticker["DDD"].spells is not None
    assert by_ticker["DDD"].spells[0].removal_announced_on is None


def test_a_schema_2_manifest_reads_with_no_history() -> None:
    # Even if someone adds the fields to an old manifest: its hash does not
    # cover them, so they are not read.
    raw = _raw("v2-point-in-time.json")
    raw["history_sessions"] = 253
    raw["instruments"][0]["history_from"] = "2025-06-25"

    manifest = DatasetManifest.from_json(raw)

    assert manifest.history_sessions == 0
    assert all(instrument.history_from is None for instrument in manifest.instruments)
    assert content_hash_of(manifest) == manifest.content_hash


@pytest.mark.parametrize(
    "edit",
    [
        lambda raw: raw.__setitem__("history_sessions", 252),
        lambda raw: raw["instruments"][0].__setitem__("history_from", "2025-06-24"),
        lambda raw: raw["instruments"][4].__setitem__("history_from", "2025-06-25"),
        lambda raw: raw["instruments"][2]["spells"][0].__setitem__("removal_announced_on", None),
        lambda raw: raw["instruments"][0]["spells"][0].__setitem__(
            "removal_announced_on", "2026-08-01"
        ),
    ],
)
def test_an_edited_history_claim_no_longer_matches_its_hash(
    edit: Callable[[dict[str, Any]], None],
) -> None:
    # history_from and history_sessions decide which rows a reader accepts
    # outside the spells; widening either by hand must not verify.
    raw = _raw("v3-history.json")
    edit(raw)

    manifest = DatasetManifest.from_json(raw)

    assert content_hash_of(manifest) != manifest.content_hash


@pytest.mark.parametrize("value", [True, -1, "253", 2.5])
def test_a_history_count_that_is_not_a_whole_number_is_refused(value: object) -> None:
    raw = _raw("v3-history.json")
    raw["history_sessions"] = value

    with pytest.raises(UnsupportedManifestError, match="history_sessions"):
        DatasetManifest.from_json(raw)


def test_a_boolean_written_as_a_string_is_refused() -> None:
    # bool("false") is True; reading it that way would relabel a raw build.
    raw = _raw("v2-point-in-time.json")
    raw["adjusted"] = "false"

    with pytest.raises(UnsupportedManifestError, match="adjusted"):
        DatasetManifest.from_json(raw)


def test_an_unknown_announcement_policy_is_refused() -> None:
    raw = _raw("v2-point-in-time.json")
    raw["announcement_policy"] = "lenient"

    with pytest.raises(UnsupportedManifestError, match="lenient"):
        DatasetManifest.from_json(raw)


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        # System.Text.Json drops a zero fraction; .NET's "O" format writes seven
        # digits whatever they are. The hash is over the "O" rendering.
        ("2026-09-18T00:00:00+00:00", "2026-09-18T00:00:00.0000000+00:00"),
        ("2026-09-18T03:04:05.5+00:00", "2026-09-18T03:04:05.5000000+00:00"),
        ("2026-09-18T03:04:05.1234567+00:00", "2026-09-18T03:04:05.1234567+00:00"),
        ("2026-09-18T03:04:05Z", "2026-09-18T03:04:05.0000000+00:00"),
    ],
)
def test_an_instant_is_rendered_the_way_dotnet_round_trips_it(text: str, expected: str) -> None:
    assert dotnet_round_trip(text) == expected


def test_a_known_as_of_instant_is_read_as_utc() -> None:
    raw = _raw("v2-point-in-time.json")
    raw["known_as_of_utc"] = "2026-09-18T03:04:05+00:00"

    manifest = DatasetManifest.from_json(raw)

    assert manifest.known_as_of_utc == datetime(2026, 9, 18, 3, 4, 5, tzinfo=UTC)
