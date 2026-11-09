#!/usr/bin/env bash
# Operator runs after reviewed source and admin assets have been staged.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
release=/home/fitmate/releases/ai-sales-20261108
site=/var/www/fitmate-admin
test -d "$site" && test ! -L "$site"
test -f "$release/admin.tgz"
exec 7>"$release/deploy.lock"
flock -n 7 || { echo 'AI sales rollout already running.' >&2; exit 1; }
stage=$(mktemp -d "$release/admin-stage.XXXXXX")
backup=$(mktemp -d "$release/admin-before.XXXXXX")
tar -tzf "$release/admin.tgz" | while IFS= read -r path; do
  case "$path" in /*|*../*) echo 'Unsafe admin artifact path.' >&2; exit 1;; esac
done
tar -xzf "$release/admin.tgz" -C "$stage"
test -s "$stage/index.html"
test -d "$stage/assets"
# Backend rollout tests against an isolated PostgreSQL database, backs up PROD,
# previews migrations on a restored private copy, then health-checks the candidate.
bash /home/fitmate/releases/mobile-update-20261026/deploy-mobile-update.sh
tar -czf "$backup/site.tgz" -C "$site" .
changed=0
rollback() {
  code=$?
  trap - ERR
  if test "$changed" = 1; then tar -xzf "$backup/site.tgz" -C "$site"; fi
  echo "Admin rollout failed; backup: $backup. Backend schema remains additive." >&2
  exit "$code"
}
trap rollback ERR
changed=1
# Keep old hashed assets for already open sessions; install index last.
cp -a "$stage/assets/." "$site/assets/"
find "$stage" -maxdepth 1 -type f ! -name index.html -exec cp -p {} "$site/" \;
install -m 0644 "$stage/index.html" "$site/index.html"
chmod -R a+rX "$site/assets"
curl --fail --silent https://admin.fitmate.site/ > "$backup/served-index.html"
cmp "$stage/index.html" "$backup/served-index.html"
status=$(curl --silent --output /dev/null --write-out '%{http_code}' https://api.fitmate.site/api/v1/admin/finance/ai-sales)
test "$status" = 401
trap - ERR
echo 'AI_SALES_ADMIN_DEPLOYED'
echo "Admin backup: $backup"
echo 'Open Finance > AI Plus orders. Real-only backfill starts within a minute; provider failures retry after 15 minutes. No bank payouts are inferred.'
