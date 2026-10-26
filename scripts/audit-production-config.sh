#!/usr/bin/env bash
# Read-only: print names/presence only. Never source or print secret values.
set -euo pipefail
set +x
env_file=/home/fitmate/backend/.env
test -r "$env_file" || { echo 'CONFIG_NOT_READABLE'; exit 1; }
keys=(
  Email__SmtpHost Email__SmtpUsername Email__SmtpPassword Email__SenderEmail Email__FrontendBaseUrl
  Groq__ApiKey Gemini__ApiKey Ai__InternalToken
  BankLookup__ClientId BankLookup__ApiKey
  GoogleMeet__ClientId GoogleMeet__ClientSecret GoogleMeet__RedirectUri
  Sms__AccessToken Sms__Sender
  Payos__ClientId Payos__ApiKey Payos__ChecksumKey Payos__AccountNumber Payos__ReturnUrl Payos__CancelUrl
  GooglePlayBilling__ServiceAccountJsonBase64
  Ga4__PropertyId Ga4__LandingStreamId Ga4__ServiceAccountJsonBase64
  R2_ACCOUNT_ID R2_ACCESS_KEY_ID R2_SECRET_ACCESS_KEY R2_BUCKET_NAME
)
for key in "${keys[@]}"; do
  value=$(sed -n "s/^${key}=//p" "$env_file" | tail -n 1)
  value=${value%$'\r'}
  value=${value#\"}; value=${value%\"}; value=${value#\'}; value=${value%\'}
  if [[ -z "$value" ]]; then state=MISSING
  elif [[ "$value" == *'__SET_'* || "$value" == *'__REPLACE'* || "$value" == 'changeme' ]]; then state=PLACEHOLDER
  else state=PRESENT_NOT_VALIDATED; fi
  printf '%s=%s\n' "$key" "$state"
done
unset value
# Only non-secret booleans/provider selectors are allowlisted below.
for key in Ai__Enabled Ai__FoodScanEnabled Ai__CompanionChatEnabled Ai__WorkoutAdjustEnabled Groq__Enabled Gemini__Enabled Payos__Enabled Payos__MerchantApproved GooglePlayBilling__Enabled GooglePlayBilling__SalesEnabled AiUsage__Enabled; do
  value=$(sed -n "s/^${key}=//p" "$env_file" | tail -n 1 | tr -d '\r')
  case "$value" in true|false|True|False) printf '%s=%s\n' "$key" "$value";; *) printf '%s=UNSET_OR_NONBOOLEAN\n' "$key";; esac
done
unset value
