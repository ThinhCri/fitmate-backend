# Fitmate Backend

ASP.NET Core API for the Fitmate Android app and admin web. The current projects target **.NET 10** (see `global.json` and the project files), use EF Core/Npgsql with PostgreSQL, and expose the versioned `/api/v1` contract. This repository is not, by itself, proof of production readiness.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/Fitmate.Api` | HTTP host and composition root |
| `src/Fitmate.Application` | Use cases and authorization |
| `src/Fitmate.Domain` | Domain model and rules |
| `src/Fitmate.Infrastructure` | Persistence and provider adapters |
| `tests/` | Unit and PostgreSQL integration tests |
| `docs/backend-release/` | Product baseline, API, release gates, and runbooks |

## Local development

Install the SDK specified by `global.json` and Docker with Compose. Copy `.env.example` to the untracked `.env`, fill only the settings needed locally, and keep provider credentials out of Git. Then:

```powershell
docker compose up -d postgres
docker compose --profile migrate up --build migrate
docker compose up -d --build api
docker compose ps
```

The migration service is a deliberate step; recreating a container does not reset the PostgreSQL named volume. Never delete a data volume to fix a test. The API is available on the port configured in `.env` (default `8080`); check `/health/live` and `/health/ready` separately. For a host-only build/test, use `dotnet restore Fitmate.slnx`, `dotnet build Fitmate.slnx`, and `dotnet test Fitmate.slnx`.

## Contract and operations

- [Production baseline](docs/backend-release/README.md), [requirements](docs/backend-release/business-requirements.md), and [task ledger](docs/backend-release/tasks.md)
- [OpenAPI v1](docs/backend-release/openapi/fitmate-v1.yaml) and [authorization matrix](docs/backend-release/authorization-matrix.md)
- [Local runbook](docs/backend-release/runbooks/local-dev.md), [CI runbook](docs/backend-release/runbooks/ci.md), and [release checklist](docs/backend-release/release-checklist.md)

CI builds and tests the code against disposable PostgreSQL and builds container images. It **does not deploy**, publish images, apply migrations to a real environment, or approve a release. Payment, AI, SMS, email, Firebase, and banking integrations require separately configured providers and operator acceptance. Feature flags must fail closed when a provider is unavailable; no demo payment or AI result is a production substitute.

Secrets belong in ignored local files or a deployment secret manager, never in source, CI logs, `EXPO_PUBLIC_*`, or a browser bundle. Review `AGENTS.md` before contributing.
