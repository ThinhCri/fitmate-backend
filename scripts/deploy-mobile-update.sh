#!/usr/bin/env bash
# Operator-run rollout. Preview pending migrations against a private restored copy first.
# Never source .env, delete production volumes, or automatically restore/drop user data.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
release=/home/fitmate/releases/mobile-update-20261026
backend=/home/fitmate/backend
test -f "$release/source.tgz"
test -f "$backend/compose.vps.yml"
test -f "$backend/.env"
finalizer=$(sed -n 's/^AccountDeletion__FinalizerEnabled=//p' "$backend/.env" | tail -n 1 | tr -d '\r')
case "$finalizer" in
  ''|false|False) ;;
  *) echo 'Final erasure config needs explicit operator review before rollout.' >&2; exit 1;;
esac
test ! -L "$backend"
exec 9>"$release/deploy.lock"
flock -n 9 || { echo 'Deployment already running.' >&2; exit 1; }
stage=$(mktemp -d "$release/source.XXXXXX")
backup=$(mktemp -d "$release/before.XXXXXX")
tar -xzf "$release/source.tgz" -C "$stage"
files=(
  src/Fitmate.Api/Program.cs
  src/Fitmate.Api/Auth/AuthController.cs
  src/Fitmate.Api/Identity/AdminAccountDeletionController.cs
  src/Fitmate.Api/Trainers/AdminCertificatesController.cs
  src/Fitmate.Api/Identity/AccountDeletionController.cs
  src/Fitmate.Api/System/SystemCapabilitiesController.cs
  src/Fitmate.Api/Admin/AdminMetricsController.cs
  src/Fitmate.Application/Identity/AccountDeletionDtos.cs
  src/Fitmate.Application/Identity/AccountDeletionService.cs
  src/Fitmate.Application/Identity/IAccountDeletionService.cs
  src/Fitmate.Application/Identity/DeletionOtpEmailOutboxHandler.cs
  src/Fitmate.Application/Identity/AdminAccountDeletionService.cs
  src/Fitmate.Application/Identity/IUserProfileService.cs
  src/Fitmate.Application/Identity/UserProfileService.cs
  src/Fitmate.Application/Storage/IOwnedStorageKeyListing.cs
  src/Fitmate.Application/Trainers/CertificateService.cs
  src/Fitmate.Application/Trainers/ICertificateService.cs
  src/Fitmate.Application/Trainers/Stores.cs
  src/Fitmate.Domain/Common/Enums.cs
  src/Fitmate.Domain/Identity/ApplicationUser.cs
  src/Fitmate.Domain/Identity/UserProfile.cs
  src/Fitmate.Infrastructure/Auth/IdentityAccountDeletionOtpGateway.cs
  src/Fitmate.Infrastructure/Auth/SpeedSmsOtpSender.cs
  src/Fitmate.Infrastructure/Booking/EfBookingStores.cs
  src/Fitmate.Infrastructure/Identity/AccountDeletionFinalizer.cs
  src/Fitmate.Infrastructure/Identity/EfAccountDeletionStore.cs
  src/Fitmate.Infrastructure/Identity/EfUserProfileStore.cs
  src/Fitmate.Infrastructure/Persistence/Configurations/ExerciseCatalogConfiguration.cs
  src/Fitmate.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs
  src/Fitmate.Infrastructure/Persistence/Migrations/20261021132000_AdminDeletionLifecycle.cs
  src/Fitmate.Infrastructure/Persistence/Migrations/20261021133000_UniqueContactPhone.cs
  src/Fitmate.Infrastructure/Persistence/Migrations/FitmateDbContextModelSnapshot.cs
  src/Fitmate.Infrastructure/Storage/LocalStorageService.cs
  src/Fitmate.Infrastructure/Storage/R2StorageService.cs
  src/Fitmate.Infrastructure/Trainers/EfTrainerMarketplaceStore.cs
  src/Fitmate.Infrastructure/Trainers/EfTrainerStores.cs
  src/Fitmate.Application/Workout/WorkoutService.cs
  src/Fitmate.Application/Finance/PtPayoutAccountService.cs
  src/Fitmate.Infrastructure/Finance/EfPtPayoutAccountStore.cs
  src/Fitmate.Application/Admin/AdminAnalyticsService.cs
  src/Fitmate.Infrastructure/Admin/Ga4ReportProvider.cs
  src/Fitmate.Infrastructure/Notifications/MissingPushProvider.cs
)
existing=()
new=()
for file in "${files[@]}"; do
  test -f "$stage/$file" && test ! -L "$stage/$file"
  if test -f "$backend/$file"; then existing+=("$file"); else new+=("$file"); fi
done
tar -czf "$backup/source-before.tgz" -C "$backend" "${existing[@]}"
printf '%s\n' "${new[@]}" > "$backup/new-files.txt"
old_image=$(docker inspect --format '{{.Image}}' fitmate-prod-api-1)
docker tag "$old_image" "fitmate-api:rollback-$(basename "$backup")"
changed=0
schema_attempted=0
test_container="fitmate-update-test-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
test_network="$test_container-net"
cleanup() {
  if test "$(docker inspect --format '{{index .Config.Labels "fitmate.role"}}' "$test_container" 2>/dev/null || true)" = isolated-release-test; then
    docker rm -fv "$test_container" >/dev/null || true
  fi
  docker network rm "$test_network" >/dev/null 2>&1 || true
}
rollback() {
  code=$?
  trap - ERR
  if test "$changed" = 1; then
    tar -xzf "$backup/source-before.tgz" -C "$backend"
    for file in "${new[@]}"; do
      case "$file" in src/*) rm -f -- "$backend/$file";; *) exit 1;; esac
    done
    docker tag "$old_image" fitmate-api:vps
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) || true
  fi
  echo "UPDATE_FAILED. Recovery: $backup" >&2
  if test "$schema_attempted" = 1; then
    echo 'Schema may have been upgraded; it is retained for compatibility. Never auto-restore the database: that could discard new user writes. Review migration log and protected dump.' >&2
  else echo 'Production database/configuration not changed.' >&2; fi
  exit "$code"
}
trap cleanup EXIT
trap rollback ERR

echo 'Running isolated PostgreSQL verification before changing production...'
docker network create --label fitmate.role=isolated-release-test "$test_network" >/dev/null
docker run -d --name "$test_container" --network "$test_network" --label fitmate.role=isolated-release-test \
  -e POSTGRES_USER=fitmate -e POSTGRES_PASSWORD=isolated-test-only -e POSTGRES_DB=fitmate \
  postgres:16-alpine >/dev/null
ready=0
for attempt in {1..45}; do
  if docker exec "$test_container" pg_isready -U fitmate >/dev/null 2>&1; then ready=1; break; fi
  sleep 2
done
test "$ready" = 1
# No production .env/credentials, network, ports or volumes are passed to either test container.
docker run --rm --network "$test_network" -v "$stage:/work" -w /work \
  -e DB_HOST="$test_container" -e DB_PORT=5432 -e DB_USER=fitmate -e DB_PASSWORD=isolated-test-only \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -euc '
    dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --verbosity minimal
    dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter "FullyQualifiedName~AccountDeletionOtpTests|FullyQualifiedName~CapabilitiesAndHealthTests|FullyQualifiedName~AccountDeletionIdentityConstraintsTests" --verbosity minimal
  ' 2>&1 | tee "$backup/verification.log"

echo 'Building candidate from staged source...'
candidate="fitmate-api:mobile-update-$(basename "$stage" | tr '[:upper:].' '[:lower:]-')"
docker build -t "$candidate" -f "$stage/src/Fitmate.Api/Dockerfile" "$stage" 2>&1 | tee "$backup/build.log"
migrator="$candidate-migrate"
docker build -t "$migrator" -f "$stage/Dockerfile.migrate" "$stage" 2>&1 | tee "$backup/migrate-build.log"
prod_db=$(cd "$backend" && docker compose -f compose.vps.yml ps -q postgres)
test -n "$prod_db"
echo 'Backing up production database privately, without exposing credentials...'
docker exec "$prod_db" sh -ec 'pg_dump -Fc --no-owner --no-acl -U "$POSTGRES_USER" "$POSTGRES_DB"' > "$backup/database.dump"
test -s "$backup/database.dump"
docker exec -i "$prod_db" pg_restore --list < "$backup/database.dump" > "$backup/database-contents.txt"
# Migration preview uses a disposable copy on a separate network, never production DB.
docker exec "$test_container" createdb -U fitmate fitmate_migration_preview
docker exec -i "$test_container" pg_restore --no-owner --no-acl -U fitmate -d fitmate_migration_preview < "$backup/database.dump"
docker run --rm --network "$test_network" -e DB_HOST="$test_container" -e DB_PORT=5432 \
  -e DB_NAME=fitmate_migration_preview -e DB_USER=fitmate -e DB_PASSWORD=isolated-test-only \
  "$migrator" 2>&1 | tee "$backup/migration-preview.log"
# Keep both copies private. Existing migrations fail transactionally on duplicate identities.
docker network inspect fitmate-prod_default >/dev/null
schema_attempted=1
docker run --rm --network fitmate-prod_default --env-file "$backend/.env" \
  -e DB_HOST=postgres -e DB_PORT=5432 "$migrator" 2>&1 | tee "$backup/migration-production.log"
# Preserve production-only settings/assets while installing only the reviewed source allowlist.
changed=1
for file in "${files[@]}"; do install -D -m 0644 "$stage/$file" "$backend/$file"; done
docker tag "$candidate" fitmate-api:vps
(cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api)
ready=0
for attempt in {1..45}; do
  if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi
  sleep 2
done
test "$ready" = 1
curl --fail --silent https://api.fitmate.site/api/v1/system/capabilities > "$backup/capabilities.json"
otp_status=$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST -H 'Content-Type: application/json' --data '{}' https://api.fitmate.site/api/v1/users/me/deletion-otp)
# Anonymous probe must stop at auth: do not send real email or submit deletion during deployment.
test "$otp_status" = 401
trap - ERR
echo 'MOBILE_BACKEND_UPDATE_DEPLOYED'
echo "OTP endpoint: HTTP $otp_status (authentication required; no email sent)"
echo "Backup: $backup"
echo 'Next: test email receipt/OTP/logout in the app before uploading the new Android release.'
