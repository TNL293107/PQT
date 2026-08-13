"""Tests for reading shared database configuration from the environment."""

from __future__ import annotations

import pytest

from personal_quant.environment import (
    DatabaseSettings,
    MissingConfigurationError,
    load_database_settings,
)

POSTGRES_VARIABLES = [
    "POSTGRES_HOST",
    "POSTGRES_PORT",
    "POSTGRES_DATABASE",
    "POSTGRES_USER",
    "POSTGRES_PASSWORD",
]


@pytest.fixture(autouse=True)
def _clean_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    """Start every test from an environment with no PostgreSQL variables set."""
    for name in POSTGRES_VARIABLES:
        monkeypatch.delenv(name, raising=False)


def test_reads_every_configured_value(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("POSTGRES_HOST", "postgres")
    monkeypatch.setenv("POSTGRES_PORT", "6543")
    monkeypatch.setenv("POSTGRES_DATABASE", "research")
    monkeypatch.setenv("POSTGRES_USER", "analyst")
    monkeypatch.setenv("POSTGRES_PASSWORD", "local-password")

    settings = load_database_settings()

    assert settings == DatabaseSettings(
        host="postgres",
        port=6543,
        database="research",
        username="analyst",
        password="local-password",
    )


def test_applies_local_development_defaults(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("POSTGRES_PASSWORD", "local-password")

    settings = load_database_settings()

    assert settings.host == "localhost"
    assert settings.port == 5432
    assert settings.database == "personal_quant"
    assert settings.username == "quant_user"


def test_rejects_a_missing_password() -> None:
    # A defaulted password would let a script silently attempt to connect with
    # the wrong credential, or worse, a guessable one.
    with pytest.raises(MissingConfigurationError, match="POSTGRES_PASSWORD"):
        load_database_settings()


def test_rejects_a_blank_password(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("POSTGRES_PASSWORD", "   ")

    with pytest.raises(MissingConfigurationError, match="POSTGRES_PASSWORD"):
        load_database_settings()


def test_rejects_a_non_numeric_port(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("POSTGRES_PASSWORD", "local-password")
    monkeypatch.setenv("POSTGRES_PORT", "not-a-port")

    with pytest.raises(MissingConfigurationError, match="POSTGRES_PORT"):
        load_database_settings()


def test_rejects_an_out_of_range_port(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("POSTGRES_PASSWORD", "local-password")
    monkeypatch.setenv("POSTGRES_PORT", "70000")

    with pytest.raises(MissingConfigurationError, match="POSTGRES_PORT"):
        load_database_settings()


def test_url_percent_encodes_credentials() -> None:
    # A password containing '@' or '/' would otherwise change which host the
    # URL points at.
    settings = DatabaseSettings(
        host="localhost",
        port=5432,
        database="personal_quant",
        username="quant_user",
        password="p@ss/word:1",
    )

    url = settings.sqlalchemy_url()

    assert url == (
        "postgresql+psycopg://quant_user:p%40ss%2Fword%3A1@localhost:5432/personal_quant"
    )


def test_repr_does_not_expose_the_password() -> None:
    # Settings objects end up in tracebacks and notebook output.
    settings = DatabaseSettings(
        host="localhost",
        port=5432,
        database="personal_quant",
        username="quant_user",
        password="super-secret",
    )

    assert "super-secret" not in repr(settings)
    assert "***" in repr(settings)
