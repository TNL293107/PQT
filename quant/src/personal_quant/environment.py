"""Reading the shared environment configuration.

The quant layer talks to the same PostgreSQL instance as the .NET backend and
is configured by the same ``POSTGRES_*`` variables from ``.env``. This module
is the single place those variables are read, so no research script has to
know their names or invent its own defaults.

No credential is ever defaulted here. A missing password is an error, not a
value to guess.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from urllib.parse import quote

__all__ = ["DatabaseSettings", "MissingConfigurationError", "load_database_settings"]

_DEFAULT_HOST = "localhost"
_DEFAULT_PORT = 5432
_DEFAULT_DATABASE = "personal_quant"
_DEFAULT_USER = "quant_user"

_MIN_PORT = 1
_MAX_PORT = 65535


class MissingConfigurationError(RuntimeError):
    """Raised when a required environment variable is absent or empty."""


@dataclass(frozen=True, slots=True)
class DatabaseSettings:
    """Connection settings for the terminal's PostgreSQL database."""

    host: str
    port: int
    database: str
    username: str
    password: str

    def sqlalchemy_url(self) -> str:
        """Build a SQLAlchemy-style URL.

        Every component is percent-encoded, so a password containing ``@``,
        ``:`` or ``/`` cannot break the URL or smuggle in another host.

        Returns:
            A ``postgresql+psycopg://`` URL for this database.
        """
        user = quote(self.username, safe="")
        secret = quote(self.password, safe="")
        host = quote(self.host, safe="")
        name = quote(self.database, safe="")
        return f"postgresql+psycopg://{user}:{secret}@{host}:{self.port}/{name}"

    def __repr__(self) -> str:
        """Return a representation that cannot leak the password.

        The default dataclass ``__repr__`` prints every field, which puts the
        password into tracebacks and notebook output.
        """
        return (
            f"DatabaseSettings(host={self.host!r}, port={self.port!r}, "
            f"database={self.database!r}, username={self.username!r}, "
            "password='***')"
        )


def _read(name: str, default: str) -> str:
    value = os.environ.get(name, "").strip()
    return value or default


def _read_port(name: str, default: int) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default

    try:
        port = int(raw)
    except ValueError as error:
        message = f"{name} must be an integer, got {raw!r}."
        raise MissingConfigurationError(message) from error

    if not _MIN_PORT <= port <= _MAX_PORT:
        message = f"{name} must be between {_MIN_PORT} and {_MAX_PORT}, got {port}."
        raise MissingConfigurationError(message)

    return port


def load_database_settings() -> DatabaseSettings:
    """Load PostgreSQL settings from the process environment.

    Returns:
        The resolved settings.

    Raises:
        MissingConfigurationError: If ``POSTGRES_PASSWORD`` is not set, or if
            ``POSTGRES_PORT`` is not a valid port number.
    """
    password = os.environ.get("POSTGRES_PASSWORD", "").strip()
    if not password:
        message = (
            "POSTGRES_PASSWORD is not set. Copy .env.example to .env and set it, "
            "or export it before running."
        )
        raise MissingConfigurationError(message)

    return DatabaseSettings(
        host=_read("POSTGRES_HOST", _DEFAULT_HOST),
        port=_read_port("POSTGRES_PORT", _DEFAULT_PORT),
        database=_read("POSTGRES_DATABASE", _DEFAULT_DATABASE),
        username=_read("POSTGRES_USER", _DEFAULT_USER),
        password=password,
    )
