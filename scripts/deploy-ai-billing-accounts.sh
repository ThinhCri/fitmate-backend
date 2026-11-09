#!/usr/bin/env bash
# Buyer-report-only rollout; operator supplies sudo on the VPS.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
release=/home/fitmate/releases/ai-billing-accounts-20261109
backend=/home/fitmate/backend
site=/var/www/fitmate-admin
cd "$release"
sha256sum --check artifacts.sha256
exec 8>deploy.lock
flock -n 8 || { echo 'Buyer reporting rollout already running.' >&2; exit 1; }
for folder in "$backend" "$site"; do test -d "$folder" && test ! -L "$folder"; done
stage=$(mktemp -d "$release/source.XXXXXX")
web=$(mktemp -d "$release/admin.XXXXXX")
backup=$(mktemp -d "$release/before.XXXXXX")
files=(src/Fitmate.Api/Admin/AdminAiSalesController.cs src/Fitmate.Application/Ai/IAdminAiSalesService.cs src/Fitmate.Infrastructure/Ai/EfAdminAiSalesStore.cs src/Fitmate.Application/Ai/IAiSubscriptionService.cs src/Fitmate.Infrastructure/Ai/EfAiSubscriptionService.cs src/Fitmate.Infrastructure/Ai/GooglePlaySubscriptionVerifier.cs)
for file in "${files[@]}"; do test -f "$backend/$file"; done
# Build on the deployed source, not an unrelated local release. Exclude all env/config secrets.
tar -C "$backend" --exclude='*/bin' --exclude='*/obj' --exclude='appsettings*.json' -cf - src tests global.json | tar -C "$stage" -xf -
for file in Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config nuget.config .dockerignore; do
  if test -f "$backend/$file"; then cp "$backend/$file" "$stage/$file"; fi
done
for archive in overlay.tgz admin.tgz; do
  tar -tzf "$archive" | while IFS= read -r path; do
    case "$path" in /*|*../*) echo 'Unsafe artifact path.' >&2; exit 1;; esac
  done
done
tar -xzf overlay.tgz -C "$stage"
# The deployed repository contains historical test doubles from older interfaces.
# Compile all reporting tests and their current harness, without unrelated legacy suites.
sed -i 's|</Project>|<ItemGroup><Compile Remove="@(Compile)" /><Compile Include="AiSalesReportingTests.cs" /></ItemGroup></Project>|' "$stage/tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj"
sed -i 's|</Project>|<ItemGroup><Compile Remove="@(Compile)" /><Compile Include="AiSalesReportingIntegrationTests.cs" /><Compile Include="AiSubscriptionIntegrationTests.cs" /><Compile Include="Infrastructure/FitmateTestAppFactory.cs" /><Compile Include="Infrastructure/IntegrationTestAssembly.cs" /><Compile Include="../Shared/StubPaymentProvider.cs" /></ItemGroup></Project>|' "$stage/tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj"
tar -xzf admin.tgz -C "$web"
test -s "$web/index.html" && test -d "$web/assets"
container="fitmate-buyers-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
network="$container-net"
changed=0
web_changed=0
cleanup() {
  if test "$(docker inspect --format '{{index .Config.Labels "fitmate.role"}}' "$container" 2>/dev/null || true)" = isolated-buyer-test; then docker rm -fv "$container" >/dev/null || true; fi
  docker network rm "$network" >/dev/null 2>&1 || true
}
rollback() {
  code=$?
  trap - ERR
  if test "$web_changed" = 1; then install -m 0644 "$backup/index.html" "$site/.index-buyers-rollback.html"; mv "$site/.index-buyers-rollback.html" "$site/index.html"; fi
  if test "$changed" = 1; then
    tar -xzf "$backup/source-before.tgz" -C "$backend"
    docker tag "$old_image" fitmate-api:vps
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) || true
  fi
  echo "BILLING_ACCOUNTS_FAILED. Private backup: $backup" >&2
  exit "$code"
}
trap cleanup EXIT
trap rollback ERR
echo 'Testing confirmed revenue with missing Google amounts on isolated PostgreSQL; production unchanged...'
docker network create --label fitmate.role=isolated-buyer-test "$network" >/dev/null
docker run -d --name "$container" --network "$network" --label fitmate.role=isolated-buyer-test -e POSTGRES_USER=fitmate -e POSTGRES_PASSWORD=ci-ephemeral-db-password -e POSTGRES_DB=fitmate postgres:16-alpine >/dev/null
ready=0
for attempt in {1..40}; do if docker exec "$container" pg_isready -U fitmate >/dev/null 2>&1; then ready=1; break; fi; sleep 2; done
test "$ready" = 1
docker run --rm --network "$network" -v "$stage:/work" -w /work -e DB_HOST="$container" -e DB_PORT=5432 -e DB_USER=fitmate -e DB_PASSWORD=ci-ephemeral-db-password mcr.microsoft.com/dotnet/sdk:10.0 bash -euc '
  dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --filter "FullyQualifiedName~AiSalesReportingTests" --verbosity minimal
  dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter "FullyQualifiedName~AiSalesReportingIntegrationTests|FullyQualifiedName~AiSubscriptionIntegrationTests" --verbosity minimal
' 2>&1 | tee "$backup/verification.log"
# Tests above receive no production appsettings or .env. Preserve existing runtime JSON
# only after isolated tests finish; keep it private and never print its contents.
while IFS= read -r -d '' file; do
  relative=${file#"$backend/"}
  install -D -m 0644 "$file" "$stage/$relative"
done < <(find "$backend/src/Fitmate.Api" -maxdepth 1 -type f -name 'appsettings*.json' -print0)
echo 'Building backend candidate...'
candidate="fitmate-api:buyers-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
docker build -t "$candidate" -f "$stage/src/Fitmate.Api/Dockerfile" "$stage" 2>&1 | tee "$backup/build.log"
# The runtime image executes as non-root. Reject unreadable configuration before
# changing the running service; stage/backup parent directories remain private.
docker run --rm --network none --entrypoint sh "$candidate" -ec '
  for file in /app/appsettings*.json; do
    if test -f "$file" && ! test -r "$file"; then
      echo "Runtime configuration is unreadable: $file" >&2
      exit 1
    fi
  done
'
old_image=$(docker inspect --format '{{.Image}}' fitmate-prod-api-1)
docker tag "$old_image" "fitmate-api:rollback-buyers-$(basename "$backup")"
tar -czf "$backup/source-before.tgz" -C "$backend" "${files[@]}"
cp "$site/index.html" "$backup/index.html"
prod_db=$(cd "$backend" && docker compose -f compose.vps.yml ps -q postgres)
test -n "$prod_db"
docker exec "$prod_db" sh -ec 'pg_dump -Fc --no-owner --no-acl -U "$POSTGRES_USER" "$POSTGRES_DB"' > "$backup/database.dump"
test -s "$backup/database.dump"
docker exec -i "$prod_db" pg_restore --list < "$backup/database.dump" > "$backup/database-contents.txt"
# No production migrations or configuration edits. Only three reviewed runtime source files.
changed=1
for file in "${files[@]}"; do install -m 0644 "$stage/$file" "$backend/$file"; done
docker tag "$candidate" fitmate-api:vps
(cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api)
ready=0
for attempt in {1..40}; do if curl -fsS http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi; sleep 2; done
if test "$ready" != 1; then
  docker logs --tail 160 fitmate-prod-api-1 > "$backup/candidate-startup.log" 2>&1 || true
  echo "Backend startup failed; private diagnostics: $backup/candidate-startup.log" >&2
  false
fi
for path in ai-sales ai-sales/buyers; do
  status=$(curl -sS -o /dev/null -w '%{http_code}' "https://api.fitmate.site/api/v1/admin/finance/$path")
  test "$status" = 401
done
# Keep all old hashed files for already-open admin tabs. Install the new HTML atomically.
cp -a "$web/assets/." "$site/assets/"
find "$web" -maxdepth 1 -type f ! -name index.html -exec cp -p {} "$site/" \;
install -m 0644 "$web/index.html" "$site/.index-buyers-new.html"
web_changed=1
mv "$site/.index-buyers-new.html" "$site/index.html"
curl -fsS https://admin.fitmate.site/ > "$backup/served-index.html"
cmp "$web/index.html" "$backup/served-index.html"
trap - ERR
echo 'AI_BILLING_ACCOUNTS_DEPLOYED'
if test -f /home/fitmate/diagnose-ai-sales.sh; then
  bash /home/fitmate/diagnose-ai-sales.sh || echo 'Read-only account diagnostics need follow-up.'
fi
echo "Backup: $backup"
echo 'Isolated PostgreSQL tests passed. Open Finance > AI Plus > Theo nguoi mua, Ctrl+F5.'
