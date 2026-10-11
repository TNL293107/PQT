"""The quant worker's place in Compose: optional, and nothing waits on it.

ADR-016 asks for a test that a crashing worker never affects API health. The
API's health depends only on what it depends on, so the property is
structural: no service may depend on the worker, and the worker must not run
unless asked for.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml

COMPOSE = Path(__file__).resolve().parents[2] / "docker-compose.yml"


def _services() -> dict[str, Any]:
    document: dict[str, Any] = yaml.safe_load(COMPOSE.read_text(encoding="utf-8"))
    services: dict[str, Any] = document["services"]
    return services


def _dependencies(service: dict[str, Any]) -> set[str]:
    depends = service.get("depends_on", {})
    return set(depends) if isinstance(depends, (dict, list)) else set()


def test_nothing_depends_on_the_worker() -> None:
    services = _services()

    assert "quant-worker" in services
    assert all(
        "quant-worker" not in _dependencies(service)
        for name, service in services.items()
        if name != "quant-worker"
    )


def test_the_worker_runs_only_when_asked_for() -> None:
    # Behind a profile: `docker compose up` starts the terminal without it.
    assert _services()["quant-worker"].get("profiles") == ["research"]


def test_the_worker_reads_datasets_and_never_writes_them() -> None:
    volumes = _services()["quant-worker"]["volumes"]

    assert "dataset-data:/app/datasets:ro" in volumes


def test_the_worker_is_given_no_owner_credentials() -> None:
    # It connects as quant_research; the owner's password must not reach it.
    environment = _services()["quant-worker"]["environment"]

    assert "POSTGRES_PASSWORD" not in environment
    assert "POSTGRES_USER" not in environment
    assert "RESEARCH_DB_PASSWORD" in environment
