#!/usr/bin/env bash
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.'; exit 1; }
release=/home/fitmate/releases/admin-pagination-20261110
backend=/home/fitmate/backend
cd "$release"
exec 8>deploy.lock
flock -n 8 || { echo 'Deployment already running'; exit 1; }
sha256sum --check artifacts.sha256
test -d "$backend" && test ! -L "$backend"
stage=$(mktemp -d "$release/source.XXXXXX")
backup=$(mktemp -d "$release/before.XXXXXX")
file=src/Fitmate.Infrastructure/Identity/EfAdminUserStore.cs
container="fitmate-pagination-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
network="$container-net"
changed=0
cleanup() {
  if test "$(docker inspect --format '{{index .Config.Labels "fitmate.role"}}' "$container" 2>/dev/null || true)" = isolated-pagination-test; then docker rm -fv "$container" >/dev/null || true; fi
  docker network rm "$network" >/dev/null 2>&1 || true
}
rollback() {
  code=$?
  trap - ERR
  if test "$changed" = 1; then
    install -m 0644 "$backup/EfAdminUserStore.cs" "$backend/$file"
    docker tag "$old_image" fitmate-api:vps
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) || true
  fi
  echo "ADMIN_PAGINATION_FAILED. Private logs: $backup"
  exit "$code"
}
trap cleanup EXIT
trap rollback ERR
tar -C "$backend" --exclude='*/bin' --exclude='*/obj' --exclude='appsettings*.json' -cf - src tests global.json | tar -C "$stage" -xf -
for config in Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config nuget.config .dockerignore; do
  if test -f "$backend/$config"; then cp "$backend/$config" "$stage/$config"; fi
done
for entry in "$file" tests/Fitmate.IntegrationTests/AdminUserPaginationTests.cs tests/Fitmate.IntegrationTests/Infrastructure/FitmateTestAppFactory.cs tests/Fitmate.IntegrationTests/Infrastructure/IntegrationTestAssembly.cs tests/Shared/StubPaymentProvider.cs; do
  tar -tzf overlay.tgz | grep -Fx "$entry" >/dev/null
done
tar -xzf overlay.tgz -C "$stage"
sed -i 's|</Project>|<ItemGroup><Compile Remove="@(Compile)" /><Compile Include="AdminUserPaginationTests.cs" /><Compile Include="Infrastructure/FitmateTestAppFactory.cs" /><Compile Include="Infrastructure/IntegrationTestAssembly.cs" /><Compile Include="../Shared/StubPaymentProvider.cs" /></ItemGroup></Project>|' "$stage/tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj"
docker network create "$network" >/dev/null
docker run -d --name "$container" --label fitmate.role=isolated-pagination-test --network "$network" -e POSTGRES_USER=fitmate -e POSTGRES_PASSWORD=ci-ephemeral-db-password -e POSTGRES_DB=postgres postgres:17-alpine >/dev/null
ready=0
for attempt in {1..40}; do if docker exec "$container" pg_isready -U fitmate >/dev/null 2>&1; then ready=1; break; fi; sleep 2; done
test "$ready" = 1
docker run --rm --network "$network" -v "$stage:/work" -w /work -e DB_HOST="$container" -e DB_PORT=5432 -e DB_USER=fitmate -e DB_PASSWORD=ci-ephemeral-db-password mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter FullyQualifiedName~AdminUserPaginationTests --verbosity minimal 2>&1 | tee "$backup/verification.log"
while IFS= read -r -d '' config; do
  relative=${config#"$backend/"}
  install -D -m 0644 "$config" "$stage/$relative"
done < <(find "$backend/src/Fitmate.Api" -maxdepth 1 -type f -name 'appsettings*.json' -print0)
candidate="fitmate-api:pagination-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
docker build -t "$candidate" -f "$stage/src/Fitmate.Api/Dockerfile" "$stage" 2>&1 | tee "$backup/build.log"
old_image=$(docker inspect --format '{{.Image}}' fitmate-prod-api-1)
docker tag "$old_image" "fitmate-api:rollback-pagination-$(basename "$backup")"
cp -p "$backend/$file" "$backup/EfAdminUserStore.cs"
changed=1
install -m 0644 "$stage/$file" "$backend/$file"
docker tag "$candidate" fitmate-api:vps
(cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api)
ready=0
for attempt in {1..40}; do if curl -fsS http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi; sleep 2; done
test "$ready" = 1
status=$(curl -sS -o /dev/null -w '%{http_code}' 'https://api.fitmate.site/api/v1/admin/users?role=pt&page=1&pageSize=10')
test "$status" = 401
trap - ERR
echo 'ADMIN_PAGINATION_DEPLOYED'
echo "Backup: $backup"
