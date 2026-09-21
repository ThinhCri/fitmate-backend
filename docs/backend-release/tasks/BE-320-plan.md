# BE-320 — OpenAPI Linter + Contract Test — Plan

> **Goal:** validate OpenAPI spec on every CI run to catch breaking changes before merge.

## 1. Tool

**Spectral** (`stoplightio/spectral`) — CLI linter for OpenAPI 3.x.
- Runs on the YAML file, no code compilation needed.
- Integrates as a CI step (`npm install -g spectral` or `npx @stoplight/spectral`).
- `.spectral.yml` ruleset committed in repo.

## 2. Existing state (verified 2026-09-21)

- OpenAPI file: `docs/backend-release/openapi/fitmate-v1.yaml` (~3500 lines).
- No linter setup exists.
- CI workflow: `.github/workflows/ci.yml` — 3 jobs (lint-test, docker-build, smoke).

## 3. Scope

### In scope

1. **Spectral CLI** — add to `lint-test` job in `ci.yml`.
2. **`.spectral.yml` ruleset** — `extends: spectral:oas` + overrides for Fitmate conventions:
   - `operationId` uniqueness enforced.
   - `summary` required on every operation.
   - `description` required on complex schemas.
   - No `Bearer` prefix in security schemes (use `Authorization` header).
   - `400`/`401`/`403`/`404`/`409`/`500` responses required on mutations.
   - No `$ref` to external files.
3. **CI job integration** — `lint-test` job runs `spectral lint`; non-zero exit on violations.
4. **Initial lint fix** — fix any violations found in existing spec.

### Out of scope

- Auto-generation of client SDKs.
- Contract tests (API integration tests with real HTTP calls — deferred to integration test suite).

## 4. Acceptance criteria

- `npx @stoplight/spectral lint docs/backend-release/openapi/fitmate-v1.yaml` exits 0 on HEAD.
- CI `lint-test` job fails if Spectral finds errors.
- No new commits needed for the spec itself (unless violations found).

## 5. Commit phases

1. **`ci(be): add Spectral OpenAPI linter to lint-test job [BE-320]`** — `.spectral.yml` + CI update + initial lint pass.

Total: 1 commit.
