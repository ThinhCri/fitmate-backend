#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
test "$(id -u)" -eq 0 || { echo 'Run with sudo'; exit 1; }

backend=/home/fitmate/backend
stage=/home/fitmate/r2-upload.env.stage
test -s "$stage" || { echo 'Missing staged R2 credentials'; exit 1; }
# PowerShell sends CRLF through SSH; remove CR before sourcing shell variables.
python3 - "$stage" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
data = path.read_bytes()
path.write_bytes(data.replace(b'\r\n', b'\n'))
PY
cd "$backend"
exec 9>/home/fitmate/r2-upload.lock
flock -n 9

backup=$(mktemp -d /home/fitmate/releases/r2-before.XXXXXX)
cp -p .env "$backup/env.before"
cp -p compose.vps.yml "$backup/compose.before"
api=$(docker compose -f compose.vps.yml ps -q api)
test -n "$api" || { echo 'API container not found'; exit 1; }
volume=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/data/uploads"}}{{.Name}}{{end}}{{end}}' "$api")
test -n "$volume" || { echo 'Uploads volume not found'; exit 1; }

set -a
source "$stage"
set +a
for key in R2_ACCOUNT_ID R2_ACCESS_KEY_ID R2_SECRET_ACCESS_KEY R2_BUCKET_NAME; do
  test -n "${!key:-}" || { echo "Missing $key"; exit 1; }
done
[[ "$R2_ACCOUNT_ID" =~ ^[0-9a-f]{32}$ ]] || { echo 'Invalid R2 account ID format'; exit 1; }
endpoint="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
aws_env=$(mktemp "$backup/aws-env.XXXXXX")
trap 'rm -f "$aws_env"' EXIT
printf 'AWS_ACCESS_KEY_ID=%s\nAWS_SECRET_ACCESS_KEY=%s\nAWS_DEFAULT_REGION=auto\nAWS_EC2_METADATA_DISABLED=true\n' \
  "$R2_ACCESS_KEY_ID" "$R2_SECRET_ACCESS_KEY" > "$aws_env"
chmod 600 "$aws_env"

aws_r2() {
  docker run --rm --env-file "$aws_env" -v "$volume:/uploads:ro" \
    amazon/aws-cli:latest --endpoint-url "$endpoint" "$@"
}

# Confirm credentials and bucket before reading any private upload.
aws_r2 s3api head-object --bucket "$R2_BUCKET_NAME" --key exercise-catalog/Pushups/0.jpg >/dev/null

count=0
while IFS= read -r -d '' key; do
  key=${key#./}
  case "$key" in
    avatars/*|certificates/*|chat/*|posts/*) ;;
    *) echo "Unexpected upload path: $key"; exit 1 ;;
  esac
  local_md5=$(docker run --rm -v "$volume:/uploads:ro" alpine:3.20 md5sum "/uploads/$key" | cut -d ' ' -f 1)
  if remote_etag=$(aws_r2 s3api head-object --bucket "$R2_BUCKET_NAME" --key "$key" --query ETag --output text 2>/dev/null); then
    remote_etag=${remote_etag//\"/}
    test "$remote_etag" = "$local_md5" || { echo "Conflict for $key; no overwrite"; exit 1; }
    echo "Already matched: $key"
  else
    aws_r2 s3api put-object --bucket "$R2_BUCKET_NAME" --key "$key" --body "/uploads/$key" >/dev/null
    remote_etag=$(aws_r2 s3api head-object --bucket "$R2_BUCKET_NAME" --key "$key" --query ETag --output text)
    remote_etag=${remote_etag//\"/}
    test "$remote_etag" = "$local_md5" || { echo "Upload verification failed: $key"; exit 1; }
    echo "Copied and verified: $key"
  fi
  count=$((count + 1))
done < <(docker run --rm -v "$volume:/uploads:ro" alpine:3.20 sh -c 'cd /uploads && find . -type f -print0')
test "$count" -ge 10 || { echo "Upload inventory unexpectedly short: $count"; exit 1; }

python3 - "$stage" .env <<'PY'
from pathlib import Path
import sys

stage, env = map(Path, sys.argv[1:])
names = ('R2_ACCOUNT_ID', 'R2_ACCESS_KEY_ID', 'R2_SECRET_ACCESS_KEY', 'R2_BUCKET_NAME')
pairs = dict(line.split('=', 1) for line in stage.read_text().splitlines() if '=' in line)
if any(not pairs.get(name) for name in names):
    raise SystemExit('Incomplete R2 configuration')
updates = {name: pairs[name] for name in names}
updates['STORAGE_BACKEND'] = 'r2'
lines = [line for line in env.read_text().splitlines()
         if not any(line.startswith(name + '=') for name in updates)]
lines.extend(name + '=' + value for name, value in updates.items())
env.write_text('\n'.join(lines) + '\n')
PY
chmod 600 .env
docker compose -f compose.vps.yml config --quiet
docker compose -f compose.vps.yml up -d --no-deps --force-recreate api
for attempt in $(seq 1 40); do
  if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null; then break; fi
  sleep 2
done
curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null
rm -f "$aws_env" "$stage"
echo "R2_ENABLED UPLOAD_FILES_VERIFIED=$count"
echo "Backup: $backup"
