#!/usr/bin/env bash
# Operator runs after reviewing the staged rollout. No real purchase or new sale is enabled.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
release=/home/fitmate/releases/ai-plus-20261108
backend=/home/fitmate/backend
exec 8>"$release/setup.lock"
flock -n 8 || { echo 'AI Plus setup is already running.' >&2; exit 1; }
test -s "$release/billing.env"
test -f "$backend/.env"
test ! -L "$backend/.env"
# Source rollout performs isolated PostgreSQL/unit tests and a protected DB backup before migration.
bash /home/fitmate/releases/mobile-update-20261026/deploy-mobile-update.sh
backup=$(mktemp -d "$release/config-before.XXXXXX")
cp -p "$backend/.env" "$backup/backend.env"
changed=0
rollback() {
  code=$?
  trap - ERR
  if test "$changed" = 1; then
    cp -p "$backup/backend.env" "$backend/.env"
    (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) >/dev/null 2>&1 || true
  fi
  echo "AI_PLUS_CONFIG_FAILED; private configuration backup: $backup" >&2
  exit "$code"
}
trap rollback ERR
# Mark before replacing configuration so restart/check failures restore the old file.
changed=1
python3 - "$backend/.env" "$release/billing.env" <<'PY'
import base64, json, pathlib, sys
path, credential = map(pathlib.Path, sys.argv[1:])
lines = path.read_text().splitlines()
def parse(rows):
    return {k.strip(): v.strip().strip('\"').strip("'") for row in rows if row and not row.lstrip().startswith('#') and '=' in row for k,v in [row.split('=',1)]}
existing = parse(lines)
secret = parse(credential.read_text().splitlines()).get('GooglePlayBilling__ServiceAccountJsonBase64','')
try:
    key = json.loads(base64.b64decode(secret, validate=True))
except Exception:
    raise SystemExit('Invalid staged billing credential format; no key printed')
if key.get('type') != 'service_account' or key.get('client_email') != 'fitmate-play-billing@fitmate-afbae.iam.gserviceaccount.com' or key.get('token_uri') != 'https://oauth2.googleapis.com/token':
    raise SystemExit('Unexpected billing service account; no key printed')
for setting in ['Ai__Enabled','Ai__CompanionChatEnabled','Ai__WorkoutAdjustEnabled']:
    if existing.get(setting,'').lower() != 'true':
        raise SystemExit('Real AI capability must already be enabled: '+setting)
provider = existing.get('Ai__CompanionProvider','')
if provider == 'groq':
    ready = existing.get('Groq__Enabled','').lower() == 'true' and bool(existing.get('Groq__ApiKey'))
elif provider == 'gemini':
    ready = existing.get('Gemini__Enabled','').lower() == 'true' and bool(existing.get('Gemini__ApiKey'))
else:
    ready = False
if not ready:
    raise SystemExit('Real chat provider is not configured; no key printed')
values = {
    'GooglePlayBilling__ServiceAccountJsonBase64': secret,
    'GooglePlayBilling__Enabled': 'true', 'GooglePlayBilling__SalesEnabled': 'false',
    'GooglePlayBilling__PackageName': 'vn.fitmate.app', 'GooglePlayBilling__ProductId': 'fitmate_ai_plus',
    'GooglePlayBilling__BasePlanId': 'monthly', 'GooglePlayBilling__MinimumAndroidVersionCode': '5',
    'AiUsage__Enabled': 'true', 'AiUsage__FreeChatPerDay': '10', 'AiUsage__PlusChatPerDay': '100',
    'AiUsage__FreeScansPerDay': '0', 'AiUsage__PlusScansPerDay': '0', 'Ai__FoodScanEnabled': 'false'
}
updated = [row for row in lines if row.split('=',1)[0].strip() not in values]
updated += [k+'='+v for k,v in values.items()]
path.write_text('\n'.join(updated)+'\n')
PY
(cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api)
ready=0
for attempt in {1..45}; do
  if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi
  sleep 2
done
test "$ready" = 1
for route in ai-usage ai-subscription; do
  status=$(curl --silent --output /dev/null --write-out '%{http_code}' "https://api.fitmate.site/api/v1/me/$route")
  test "$status" = 401
done
trap - ERR
rm -f -- "$release/billing.env"
echo 'AI_PLUS_VERIFICATION_AND_QUOTAS_CONFIGURED'
echo 'New sales: OFF. Food scan: OFF. Shared daily chat/workout: Free 10 / Plus 100.'
echo "Private config backup: $backup"
echo 'Next: build code 5 for internal Play testing; enable test checkout only when that build is installed.'
echo 'Real purchase verification, acknowledgement and restore still require device acceptance tests.'
