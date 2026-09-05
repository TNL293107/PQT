# Local Setup

Everything needed to build, run and test the repository.

## Prerequisites

| Tool           | Version    | Needed for                          |
| -------------- | ---------- | ----------------------------------- |
| .NET SDK       | 10.0+      | Backend                             |
| Node.js        | 22+        | Frontend                            |
| Python         | 3.12+      | Quant layer                         |
| CMake          | 3.24+      | C++ engine                          |
| C++ compiler   | C++20      | MSVC 19.3x+, GCC 12+, Clang 15+     |
| Docker Desktop | recent     | PostgreSQL, Redis, full environment |
| Git            | 2.40+      | Everything                          |

You do not need all of them. Each stack builds independently — the C++ engine
does not require .NET, and the frontend does not require Python.

## 1. Configure the environment

```bash
cp .env.example .env
```

On Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

Then open `.env` and replace `CHANGE_ME` with a real local password.

`.env` is git-ignored and must never be committed. The provider and broker keys
are unused in Phase 0 — leave them blank. `LIVE_TRADING_ENABLED` must stay
`false`.

Compose fails immediately with a readable message if a required value is
missing, rather than starting an unauthenticated database.

## 2. Run everything with Docker

```bash
docker compose up --build
```

| Service    | URL                                  |
| ---------- | ------------------------------------ |
| Terminal   | http://localhost:3100                |
| Liveness   | http://localhost:8080/health         |
| Readiness  | http://localhost:8080/health/ready   |
| OpenAPI UI | http://localhost:8080/scalar/v1      |
| PostgreSQL | localhost:5432                       |
| Redis      | localhost:6379                       |

The OpenAPI document and its UI are served in the `Development` environment
only — there is no authentication yet, so the schema is not exposed elsewhere.

Verify:

```bash
curl http://localhost:8080/health/ready
```

Expect HTTP 200 and both dependencies healthy:

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 8.1,
      "description": "PostgreSQL responded to a round-trip query." },
    { "name": "redis", "status": "Healthy", "durationMs": 2.3,
      "description": "Redis responded to PING." }
  ]
}
```

Stop, keeping data:

```bash
docker compose down
```

Stop and wipe the database and cache volumes:

```bash
docker compose down -v
```

## 3. Day-to-day: run services on the host

Containerising the backend on every edit is slow. The usual loop is to run only
the data services in Docker and the application on the host.

```bash
docker compose up -d postgres redis
```

### Backend

The password is not defaulted in any committed file. Store it in user secrets
once:

```bash
dotnet user-secrets --project backend/src/PersonalQuant.Api set "Postgres:Password" "your-local-password"
```

Then:

```bash
dotnet run --project backend/src/PersonalQuant.Api
```

The API listens on http://localhost:8080. In `Development` it defaults to
`localhost` for both dependencies and applies pending migrations on start.

### Frontend

```bash
npm install --prefix frontend
```

```bash
npm run dev --prefix frontend
```

Vite serves on http://localhost:3100 and calls the API at the URL in
`VITE_API_BASE_URL`.

## 4. Database migrations

Schema is owned by EF Core migrations under
`backend/src/PersonalQuant.Infrastructure/Persistence/Migrations`.

Install the tool once:

```bash
dotnet tool install --global dotnet-ef
```

Add a migration:

```bash
dotnet ef migrations add MigrationName --project backend/src/PersonalQuant.Infrastructure --startup-project backend/src/PersonalQuant.Infrastructure --output-dir Persistence/Migrations
```

Apply migrations manually (needs `POSTGRES_PASSWORD` exported):

```bash
dotnet ef database update --project backend/src/PersonalQuant.Infrastructure --startup-project backend/src/PersonalQuant.Infrastructure
```

Automatic migration on start-up is controlled by
`Postgres:ApplyMigrationsOnStartup`. It is enabled in `Development` and in
Compose, and defaults to `false` everywhere else.

## 4b. Backups, before real data

Everything in the database today can be regenerated from the repository. Real
market data cannot, and neither can the observation history recorded alongside
it. Take and **verify** a backup before the first ingest of real prices:
[`database-backup.md`](database-backup.md).

## 5. Running the tests

### Backend

```bash
dotnet test backend/PersonalQuant.slnx
```

Unit tests need nothing external.

The integration tests are in two groups. The health endpoint tests run
anywhere: they boot the real API against deliberately unreachable dependencies
and assert that liveness stays healthy while readiness reports 503.

The dependency tests start throwaway PostgreSQL and Redis containers through
Testcontainers. **Without Docker running they skip with an explicit reason** —
they are never silently reported as passing. Start Docker to run them.

### Frontend

```bash
npm run lint --prefix frontend
```

```bash
npm run typecheck --prefix frontend
```

```bash
npm test --prefix frontend
```

```bash
npm run build --prefix frontend
```

### Python

Create the environment once, from `quant/`:

```bash
python -m venv .venv
```

Activate it — `.venv\Scripts\Activate.ps1` on Windows, `source .venv/bin/activate`
elsewhere — then:

```bash
pip install -e ".[dev]"
```

```bash
pytest
```

```bash
ruff check . && ruff format --check . && mypy
```

### C++

From `cpp-engine/`:

```bash
cmake --preset ci && cmake --build --preset ci && ctest --preset ci
```

The first configure downloads GoogleTest through `FetchContent`, so it needs
network access. Afterwards the build works offline. To skip the suite and the
download entirely, configure with `-DPQ_ENGINE_BUILD_TESTS=OFF`.

## 6. Full check, as CI runs it

```bash
dotnet test backend/PersonalQuant.slnx --configuration Release
```

```bash
npm ci --prefix frontend && npm run lint --prefix frontend && npm test --prefix frontend && npm run build --prefix frontend
```

```bash
cd quant && ruff check . && mypy && pytest
```

```bash
cd cpp-engine && cmake --preset ci && cmake --build --preset ci && ctest --preset ci
```

## Troubleshooting

**`docker compose up` exits with "set POSTGRES_PASSWORD in .env"**
`.env` is missing or the value is empty. Copy `.env.example` and fill it in.

**Backend starts but readiness returns 503**
Expected while PostgreSQL or Redis is still starting. If it persists, check
`docker compose ps` — the health status column tells you which dependency is
not ready. The readiness body names the failing dependency; the container logs
carry the underlying error, which is deliberately kept out of the HTTP
response.

**Frontend shows "Cannot reach the API"**
The backend is not running, or `VITE_API_BASE_URL` points at the wrong port.
Note that Vite inlines this value **at build time**, so changing it requires a
rebuild, not a restart.

**Backend cannot connect when run on the host**
`Postgres:Host` must be `localhost`, not `postgres`. The service name only
resolves inside the Compose network. `appsettings.Development.json` already
uses `localhost`.

**Integration tests skip**
Docker is not running. That is the designed behaviour, not a failure.

**`dotnet ef` is not found**
`dotnet tool install --global dotnet-ef`, then make sure `~/.dotnet/tools` is
on `PATH`.

**Vitest workers time out**
The project pins the `threads` pool in `vite.config.ts`; the default `forks`
pool fails to hand-shake on some Node and Windows combinations.

**Port already in use**
Change `API_PORT`, `FRONTEND_PORT`, `POSTGRES_HOST_PORT` or `REDIS_HOST_PORT`
in `.env`.
