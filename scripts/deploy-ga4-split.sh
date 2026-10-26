#!/usr/bin/env bash
# Run only after the reviewed files and Admin dist are staged in this release.
set -Eeuo pipefail
umask 077
test "$(id -u)" = 0 || { echo "Run with sudo bash." >&2; exit 1; }
release=/home/fitmate/releases/ga4-split-20261023
backend=/home/fitmate/backend
admin=/var/www/fitmate-admin
files=(
  src/Fitmate.Application/Admin/AdminAnalyticsService.cs
  src/Fitmate.Infrastructure/Admin/Ga4ReportProvider.cs
  src/Fitmate.Api/Admin/AdminMetricsController.cs
)
test -f "$backend/compose.vps.yml"
test -f "$backend/.env"
test -f "$release/admin/index.html"
for file in "${files[@]}"; do test -f "$release/backend/$file"; done
backup=$(mktemp -d "$release/before.XXXXXX")
tar -czf "$backup/backend.tgz" -C "$backend" .env "${files[@]}"
cp -a "$admin" "$backup/admin"
old_image=$(docker image inspect fitmate-api:vps --format '{{.Id}}')
restarted=0
admin_changed=0
rollback() {
  status=$?
  trap - ERR
  echo "Deployment failed; restoring the previous API/configuration and Admin index." >&2
  tar -xzf "$backup/backend.tgz" -C "$backend"
  docker tag "$old_image" fitmate-api:vps
  if test "$restarted" = 1; then
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps api) || true
  fi
  if test "$admin_changed" = 1; then cp -a "$backup/admin/." "$admin/"; fi
  echo "Recovery backup: $backup" >&2
  exit "$status"
}
trap rollback ERR
for file in "${files[@]}"; do install -m 0644 "$release/backend/$file" "$backend/$file"; done
cp -p "$backend/.env" "$backup/env-reference"
if grep -q '^Ga4__LandingStreamId=' "$backend/.env"; then
  sed -i 's/^Ga4__LandingStreamId=.*/Ga4__LandingStreamId=16187492175/' "$backend/.env"
else
  printf '\nGa4__LandingStreamId=16187492175\n' >> "$backend/.env"
fi
chown --reference="$backup/env-reference" "$backend/.env"
chmod --reference="$backup/env-reference" "$backend/.env"
cd "$backend"
docker compose -f compose.vps.yml build api
restarted=1
docker compose -f compose.vps.yml up -d --no-deps api
ready=0
for attempt in {1..45}; do
  if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi
  sleep 2
done
test "$ready" = 1
curl --fail --silent https://api.fitmate.site/api/v1/system/capabilities >/dev/null
admin_changed=1
mkdir -p "$admin/assets"
cp -a "$release/admin/assets/." "$admin/assets/"
chmod -R a+rX "$admin/assets"
install -m 0644 "$release/admin/index.html" "$admin/index.html.next"
mv "$admin/index.html.next" "$admin/index.html"
curl --fail --silent https://admin.fitmate.site/ >/dev/null
trap - ERR
echo "GA4_SPLIT_DEPLOYED"
echo "Backup: $backup"
