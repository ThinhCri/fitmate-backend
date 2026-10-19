# Backend CI and release boundary

`.github/workflows/ci.yml` runs on pull requests and pushes to `main` or `backend-release/*`, plus manual dispatch. It grants only `contents: read` to `GITHUB_TOKEN` and cancels obsolete runs for the same ref.

| Job | Gate | Evidence |
| --- | --- | --- |
| `secrets` | Scan committed history for exposed credentials | GitHub Actions job result |
| `verify` | Restore/build with the repository SDK; unit tests | GitHub Actions job result |
| `integration` | Tests against disposable PostgreSQL 16 | GitHub Actions job result |
| `images` | Builds API and migration images locally on the runner | GitHub Actions job result |
| `smoke` | Applies migrations in an isolated Compose project and checks API readiness | GitHub Actions job result |

The workflow neither pushes images nor deploys staging/production. A green check is code verification, **not** UAT or release approval. Configure branch protection to require all five jobs; only a human operator may approve deployment after the [release checklist](../release-checklist.md), backed-up migration plan, provider checks and device/payment UAT. Add a separately approved CD workflow only when an actual registry, target environment, scoped credentials, rollback procedure and health verification exist. Do not represent a metadata-printing job as a deployment.

CI uses fixed, disposable database credentials only inside its isolated runner. Never reuse them in a real environment or print real keys in logs. Locally, run `dotnet restore Fitmate.slnx`, `dotnet build Fitmate.slnx`, `dotnet test Fitmate.slnx`, then the isolated integration test project with a disposable database. Preserve existing Docker volumes containing user data.
