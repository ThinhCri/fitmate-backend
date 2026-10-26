#!/usr/bin/env bash
# Configuration-only operation: no code build, migrations or volume changes.
set -Eeuo pipefail
set +x
umask 077
mode=${1:---prepare}
backend=/home/fitmate/backend
release=/home/fitmate/releases/groq-chat-20261025
env_file=$backend/.env
model=qwen/qwen3.8-27b
test "$mode" = --prepare || test "$mode" = --activate
test -f "$env_file"
test -f "$backend/compose.vps.yml"
mkdir -p "$release"
if test "$mode" = --activate; then
  test "$(id -u)" = 0 || { echo 'Run activation with sudo bash.' >&2; exit 1; }
else
  if grep -qE '^Ai__Enabled=true\s*$' "$env_file"; then
    echo 'AI is already enabled; refusing to overwrite an active configuration.' >&2
    exit 1
  fi
fi
backup=$(mktemp -d "$release/config-before.XXXXXX")
cp -p "$env_file" "$backup/.env"
temp=$(mktemp "$backend/.env.groq.XXXXXX")
models=$(mktemp "$release/models.XXXXXX")
changed=0
rollback() {
  status=$?
  trap - ERR
  if test "$changed" = 1; then
    cp -p "$backup/.env" "$env_file"
    if test "$mode" = --activate; then
      (cd "$backend" && docker compose -f compose.vps.yml up -d --no-deps --force-recreate api) || true
    fi
  fi
  echo "Groq configuration failed; previous configuration retained/restored. Backup: $backup" >&2
  exit "$status"
}
cleanup() { unset key; rm -f -- "$temp" "$models"; }
trap rollback ERR
trap cleanup EXIT
key=''
if test "$mode" = --activate; then
  read -r -s -p 'Paste Groq API key (hidden; never send it in chat): ' key </dev/tty
  printf '\n' >/dev/tty
  if [[ ! "$key" =~ ^[A-Za-z0-9_-]+$ ]]; then
    echo 'Empty key or unsupported characters. No changes applied.' >&2
    exit 1
  fi
  # Secret header is passed through stdin, never process arguments or logs.
  printf 'header = "Authorization: Bearer %s"\n' "$key" |
    curl --config - --fail --silent --show-error --connect-timeout 10 --max-time 30 \
      https://api.groq.com/openai/v1/models -o "$models"
  python3 -c 'import json,sys; data=json.load(open(sys.argv[1])); sys.exit(0 if any(x.get("id")==sys.argv[2] for x in data.get("data",[])) else "Configured model is not available to this key. No changes applied.")' "$models" "$model"
fi
while IFS= read -r line || test -n "$line"; do
  case "$line" in
    Ai__Enabled=*|Ai__CompanionChatEnabled=*|Ai__FoodScanEnabled=*|Ai__WorkoutAdjustEnabled=*|Ai__CompanionProvider=*|Groq__Enabled=*|Groq__Model=*|Groq__TimeoutSeconds=*|Groq__MaxOutputTokens=*|Groq__MaxHistoryMessages=*|Gemini__Enabled=*) continue ;;
    Groq__ApiKey=*) if test "$mode" = --activate; then continue; fi ;;
  esac
  printf '%s\n' "$line"
done < "$env_file" > "$temp"
enabled=false
if test "$mode" = --activate; then enabled=true; fi
printf '\nAi__Enabled=%s\nAi__CompanionChatEnabled=%s\nAi__FoodScanEnabled=false\nAi__WorkoutAdjustEnabled=false\nAi__CompanionProvider=groq\nGroq__Enabled=true\nGroq__Model=%s\nGroq__TimeoutSeconds=60\nGroq__MaxOutputTokens=500\nGroq__MaxHistoryMessages=10\nGemini__Enabled=false\n' "$enabled" "$enabled" "$model" >> "$temp"
if test "$mode" = --activate; then printf 'Groq__ApiKey=%s\n' "$key" >> "$temp"; fi
unset key
chown --reference="$env_file" "$temp"
chmod 600 "$temp"
changed=1
mv -f -- "$temp" "$env_file"
if test "$mode" = --activate; then
  cd "$backend"
  docker compose -f compose.vps.yml up -d --no-deps --force-recreate api
  ready=0
  for attempt in {1..45}; do
    if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null; then ready=1; break; fi
    sleep 2
  done
  test "$ready" = 1
  curl --fail --silent https://api.fitmate.site/api/v1/system/capabilities |
    python3 -c 'import json,sys; a=json.load(sys.stdin)["ai"]; assert a["enabled"] and a["companion_chat"] and not a["food_scan"] and not a["workout_adjust"]'
  echo 'GROQ_CHAT_ENABLED — configuration/key/model checks passed; in-app response test still required.'
else
  echo 'GROQ_CHAT_PREPARED — no key added, no restart; public AI remains disabled until activation.'
fi
trap - ERR
echo "Backup: $backup"
