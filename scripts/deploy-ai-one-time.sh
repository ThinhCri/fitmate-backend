#!/usr/bin/env bash
# Buyer-report-only rollout; operator supplies sudo on the VPS.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
release=/home/fitmate/releases/ai-one-time-20261110
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
files=(src/Fitmate.Api/Admin/AdminAiSalesController.cs src/Fitmate.Application/Ai/IAdminAiSalesService.cs src/Fitmate.Infrastructure/Ai/EfAdminAiSalesStore.cs src/Fitmate.Application/Ai/IAiSubscriptionService.cs src/Fitmate.Infrastructure/Ai/EfAiSubscriptionService.cs src/Fitmate.Infrastructure/Ai/GooglePlaySubscriptionVerifier.cs src/Fitmate.Api/Program.cs src/Fitmate.Api/Ai/AiPlayOrderSyncWorker.cs src/Fitmate.Infrastructure/Ai/AiPlayOrderSync.cs src/Fitmate.Api/Realtime/AdminFinanceHub.cs src/Fitmate.Api/Realtime/AdminFinanceChangedHandler.cs src/Fitmate.Api/Cli/ReconcileAiOrdersCommand.cs src/Fitmate.Infrastructure/Ai/GooglePlayOrderRecovery.cs src/Fitmate.Api/Ai/AiSubscriptionController.cs src/Fitmate.Application/Ai/GooglePlayBillingOptions.cs src/Fitmate.Application/Ai/IAiOneTimePurchaseService.cs src/Fitmate.Infrastructure/Ai/GooglePlayProductVerifier.cs src/Fitmate.Infrastructure/Ai/EfAiOneTimePurchaseService.cs src/Fitmate.Infrastructure/Ai/GooglePlayOrderParser.cs)
test_files=(tests/Fitmate.UnitTests/AiSalesReportingTests.cs tests/Fitmate.UnitTests/GooglePlaySubscriptionVerifierTests.cs tests/Fitmate.UnitTests/AdminFinanceRealtimeTests.cs tests/Fitmate.UnitTests/GooglePlayProductVerifierTests.cs tests/Fitmate.UnitTests/AiOneTimeApiTests.cs tests/Fitmate.IntegrationTests/AiSalesReportingIntegrationTests.cs tests/Fitmate.IntegrationTests/GooglePlayOrderRecoveryTests.cs tests/Fitmate.IntegrationTests/AiOneTimePurchaseIntegrationTests.cs tests/Fitmate.IntegrationTests/AiSubscriptionIntegrationTests.cs tests/Fitmate.IntegrationTests/Infrastructure/FitmateTestAppFactory.cs tests/Fitmate.IntegrationTests/Infrastructure/IntegrationTestAssembly.cs tests/Shared/StubPaymentProvider.cs)
existing=()
new=()
for file in "${files[@]}"; do
  if test -f "$backend/$file"; then existing+=("$file"); else new+=("$file"); fi
done
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
# Every runtime file installed below must come from this reviewed overlay,
# never silently inherit an older copy from the deployed backend.
tar -tzf overlay.tgz > "$stage/overlay-files.txt"
for file in "${files[@]}" "${test_files[@]}"; do
  grep -Fx -- "$file" "$stage/overlay-files.txt" >/dev/null || { echo "Missing reviewed runtime file: $file" >&2; false; }
done
tar -xzf overlay.tgz -C "$stage"
# The deployed repository contains historical test doubles from older interfaces.
# Compile all reporting tests and their current harness, without unrelated legacy suites.
sed -i 's|</Project>|<ItemGroup><Compile Remove="@(Compile)" /><Compile Include="AiSalesReportingTests.cs" /><Compile Include="GooglePlaySubscriptionVerifierTests.cs" /><Compile Include="AdminFinanceRealtimeTests.cs" /><Compile Include="GooglePlayProductVerifierTests.cs" /><Compile Include="AiOneTimeApiTests.cs" /></ItemGroup></Project>|' "$stage/tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj"
sed -i 's|</Project>|<ItemGroup><Compile Remove="@(Compile)" /><Compile Include="AiSalesReportingIntegrationTests.cs" /><Compile Include="GooglePlayOrderRecoveryTests.cs" /><Compile Include="AiOneTimePurchaseIntegrationTests.cs" /><Compile Include="AiSubscriptionIntegrationTests.cs" /><Compile Include="Infrastructure/FitmateTestAppFactory.cs" /><Compile Include="Infrastructure/IntegrationTestAssembly.cs" /><Compile Include="../Shared/StubPaymentProvider.cs" /></ItemGroup></Project>|' "$stage/tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj"
tar -xzf admin.tgz -C "$web"
test -s "$web/index.html" && test -d "$web/assets"
container="fitmate-buyers-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
network="$container-net"
changed=0
web_changed=0
nginx_changed=0
nginx_file=''
cleanup() {
  if test "$(docker inspect --format '{{index .Config.Labels "fitmate.role"}}' "$container" 2>/dev/null || true)" = isolated-buyer-test; then docker rm -fv "$container" >/dev/null || true; fi
  docker network rm "$network" >/dev/null 2>&1 || true
}
rollback() {
  code=$?
  trap - ERR
  if test "$web_changed" = 1; then install -m 0644 "$backup/index.html" "$site/.index-buyers-rollback.html"; mv "$site/.index-buyers-rollback.html" "$site/index.html"; fi
  if test "$nginx_changed" = 1; then
    cp -p "$backup/nginx-before.conf" "$nginx_file"
    nginx -t && systemctl reload nginx || true
  fi
  if test "$changed" = 1; then
    tar -xzf "$backup/source-before.tgz" -C "$backend"
    cp -p "$backup/runtime.env" "$backend/.env"
    for file in "${new[@]}"; do
      case "$file" in src/Fitmate.*/*.cs) rm -f -- "$backend/$file";; *) echo "Unexpected rollback path" >&2;; esac
    done
    docker tag "$old_image" fitmate-api:vps
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) || true
  fi
  echo "AI_ONE_TIME_DEPLOY_FAILED. Private backup: $backup" >&2
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
  dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --filter "FullyQualifiedName~AiSalesReportingTests|FullyQualifiedName~AdminFinanceRealtimeTests|FullyQualifiedName~GooglePlaySubscriptionVerifierTests|FullyQualifiedName~GooglePlayProductVerifierTests|FullyQualifiedName~AiOneTimeApiTests" --verbosity minimal
  dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter "FullyQualifiedName~AiSalesReportingIntegrationTests|FullyQualifiedName~AiSubscriptionIntegrationTests|FullyQualifiedName~GooglePlayOrderRecoveryTests|FullyQualifiedName~AiOneTimePurchaseIntegrationTests" --verbosity minimal
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
tar -czf "$backup/source-before.tgz" -C "$backend" "${existing[@]}"
cp "$site/index.html" "$backup/index.html"
prod_db=$(cd "$backend" && docker compose -f compose.vps.yml ps -q postgres)
test -n "$prod_db"
docker exec "$prod_db" sh -ec 'pg_dump -Fc --no-owner --no-acl -U "$POSTGRES_USER" "$POSTGRES_DB"' > "$backup/database.dump"
test -s "$backup/database.dump"
docker exec -i "$prod_db" pg_restore --list < "$backup/database.dump" > "$backup/database-contents.txt"
# No migration. Preserve old subscription verification; enable only the approved one-time storefront for code8+.
cp -p "$backend/.env" "$backup/runtime.env"
changed=1
python3 "$release/enable-one-time.py" "$backend/.env"
for file in "${files[@]}"; do install -D -m 0644 "$stage/$file" "$backend/$file"; done
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
# Add only the admin finance hub proxy, retaining all other vhost settings.
nginx_file=$(readlink -f /etc/nginx/sites-enabled/fitmate-admin)
case "$nginx_file" in /etc/nginx/sites-available/fitmate-admin|/etc/nginx/sites-enabled/fitmate-admin) ;; *) echo 'Unexpected Nginx admin config path.' >&2; false;; esac
cp -p "$nginx_file" "$backup/nginx-before.conf"
nginx_changed=1
python3 "$release/configure-admin-finance-live.py" "$nginx_file"
nginx -t
systemctl reload nginx
live_status=$(curl -sS --max-time 15 -X POST -o /dev/null -w '%{http_code}' 'https://admin.fitmate.site/api/v1/admin/finance/live/negotiate?negotiateVersion=1')
test "$live_status" = 401
# Keep all old hashed files for already-open admin tabs. Install the new HTML atomically.
cp -a "$web/assets/." "$site/assets/"
find "$web" -maxdepth 1 -type f ! -name index.html -exec cp -p {} "$site/" \;
install -m 0644 "$web/index.html" "$site/.index-buyers-new.html"
web_changed=1
mv "$site/.index-buyers-new.html" "$site/index.html"
curl -fsS https://admin.fitmate.site/ > "$backup/served-index.html"
cmp "$web/index.html" "$backup/served-index.html"
trap - ERR
echo 'AI_ONE_TIME_BACKEND_ADMIN_DEPLOYED'
echo "Backup: $backup"
echo 'Isolated PostgreSQL tests passed. Reopen the admin tab once to load the new client; subsequent finance updates are pushed automatically.'

# Reconcile explicit provider orders only. Ownership is resolved from Google evidence,
# not operator-selected users. No charge/refund is issued; pending delivery may be acknowledged.
# Keep the corrected backend running if one order requires manual ownership review.
test -s "$release/order-ids.txt"
mapfile -t order_ids < "$release/order-ids.txt"
test "${#order_ids[@]}" -ge 1 && test "${#order_ids[@]}" -le 100
(cd "$backend" && docker compose -f compose.vps.yml exec -T api dotnet Fitmate.Api.dll reconcile-ai-orders "${order_ids[@]}")
echo 'AI_ONE_TIME_DEPLOY_COMPLETE'
echo 'New one-time checkout: ON for code8+. Legacy paid rights retained. Run signed internal-device acceptance before production Android rollout.'
if test -f /home/fitmate/check-ai-order.sh; then bash /home/fitmate/check-ai-order.sh; fi
