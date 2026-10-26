# Mobile/backend release audit — 2026-10-26

> Superseded provider implementation status on 2026-10-27: real FCM, hosted Gemini food scan, Groq workout suggestions and profile summaries now exist. See [current provider setup and real-test checklist](provider-setup-and-real-tests-20261027.md). Presence-only VPS observations below remain historical; no new live audit/deploy was performed. The old staged archive must be rebuilt.

This is a readiness audit, not certification that every product module is complete. Never enable payments, billing, AI inference or push merely by setting all booleans true.

## Configuration actually inspected on VPS

Only key names and missing/present status were output; no secret value was copied to chat or source. PRESENT means stored, not verified credentials/service permissions/end-to-end success.

| Module | Configuration required from owner | Observed / remaining work |
|---|---|---|
| Email OTP | No additional key for deletion; existing Email__SmtpHost / SmtpUsername / SmtpPassword / SenderEmail / FrontendBaseUrl | All present. Deletion uses signup's email OTP provider + existing SMTP. Latest endpoint/code/schema not deployed; receipt and verification UAT pending. |
| Groq companion | Groq__ApiKey | Present and chat flags enabled from previous operator activation. In-app response still needs verification; do not require a second Gemini key for this provider. |
| GA4 | Ga4__PropertyId, Ga4__LandingStreamId, Ga4__ServiceAccountJsonBase64 | All present. Prior App/Landing diagnostics available; latest native consent/status UX still needs device checks. |
| PT account-holder lookup | BankLookup__ClientId, BankLookup__ApiKey | Missing. Provider must be selected as VietQr only with real Lookup API access. Entering a bank account is not ownership verification. SMS phone gate removed; this does not remove actual bank lookup prerequisites. |
| Online PT / Google Meet | GoogleMeet__ClientId, GoogleMeet__ClientSecret, GoogleMeet__RedirectUri | Missing. OAuth consent screen/Calendar permissions and exact registered callback must also be configured; key presence alone does not validate meetings. |
| PT booking payments / payOS | Payos__ClientId, Payos__ApiKey, Payos__ChecksumKey, Payos__AccountNumber; Payos__ReturnUrl and Payos__CancelUrl | Missing; Payos__Enabled false. Merchant approval and webhook/payment/refund UAT required before enablement. These are not Google Play subscription credentials. |
| AI Plus via Google Play | GooglePlayBilling__ServiceAccountJsonBase64 | Missing; Enabled and SalesEnabled false. Play Console access/API permissions + correct product/base-plan and real test purchase/restore required. |
| Phone verification / SMS | Sms__AccessToken (already present), optional Sms__Sender depending on SmsType | Sender missing is not necessarily an error: types 2/4 do not require a branded sender. Verify provider/type and actual phone flow before claiming SMS operational. Not a prerequisite for PT payout-account save anymore. |
| Food scan / workout AI | Ai__InternalToken plus the actual Python inference service/artifact contract | Token missing and both features disabled. A key alone cannot supply the missing service/model. Gemini__ApiKey is also absent but optional unless owner selects Gemini. Do not silently replace the agreed AI provider/model. |
| Storage R2 | R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, R2_BUCKET_NAME | Missing, optional while using current local persistent uploads. Do not purchase resources/change storage or lose existing media to enable this. |
| Push notifications | Not a fill-in key task yet | DI hardwired MissingPushProvider; mobile has no FCM/Expo push token registration lifecycle. Needs a real provider + native client wiring, consent/token rotation/logout/revoke/deep-link tests. OS permission enabled is not proof of push delivery. |

Configuration location: private `/home/fitmate/backend/.env` (operator editor / secret manager), never `EXPO_PUBLIC_*` or Git. Do not paste secrets in chat. Old GoogleAuth/GOOGLE_CLIENT_* names are NOT requested: no matching sign-in implementation was found in the audited auth flow.

## Fixed in local source during audit

- Public capabilities now report push disabled when the selected provider is missing, instead of unconditionally true.
- Privacy/terms/deletion links now use the existing `fitmate.site` pages; support email matches the published landing page and fake `1900-FITMATE` hotline is removed.
- Missing push-provider diagnostics no longer log notification titles.
- Mobile capability cache refreshes after 60 seconds instead of retaining stale feature gates indefinitely; settings accurately describes current light-only UI rather than exposing raw `system` preference as if theme switching worked.
- Previous local fixes cover OTP email/delete/logout, timer/media, labels, icon mask, comment keyboard and release R8. Latest follow-ups are not all device-verified.

## Important additional deployment gap

Normalized source comparison (ignoring CRLF only) found the VPS source also lacks the admin deletion lifecycle from commit 82fe64f and two migration files: AdminDeletionLifecycle and UniqueContactPhone. Production migration history has not been queried (operator Docker/sudo required), so absence of source alone does not prove the DB lacks these changes. The first adds DeletionRequestedAt; the second canonicalizes contact-phone formatting and adds a unique index. Do not replace only the controller/Program files and assume the database is compatible. The existing final-erasure worker must remain disabled unless separately approved; queuing an admin request is not immediate permanent deletion.

Migration risk: duplicate active identities or normalized phone numbers cause a deliberate failure; never deduplicate/delete user data automatically. Formatting normalization is not fully reversible by Down. Protect a full dump before migration; automatic rollback retains additive schema and restores API/source, never restores a database over new user writes. Restoring a dump or running destructive Down requires an explicit maintenance/restore decision.

## Operator rollout

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\Ky8\exe201\FitmateAI_BE\scripts\prepare-mobile-update.ps1` packages tracked source only, uploads and checks archive hash + shell syntax. It does not restart production.
2. `ssh -t -i "$env:USERPROFILE\.ssh\fitmate_vps_ed25519" fitmate@103.116.39.29 "sudo bash /home/fitmate/releases/mobile-update-20261026/deploy-mobile-update.sh"` is the separate operator approval/execution step.
3. Script runs unit/isolated PostgreSQL tests first, builds candidate images, protects a private database dump, restores it only into a disposable preview DB, tests migrations there, then applies migrations to the real DB and restarts only API. No `.env` changes, paid resources, storage-volume deletion, SMTP sends or account deletion. Preview containers are removed; protected backups/logs remain under the release directory.
4. On success expect MOBILE_BACKEND_UPDATE_DEPLOYED and anonymous OTP endpoint 401 (auth required), not 404. This is smoke evidence only. Then test a consenting test account's email/code/expiry/failure/success/logout and verify existing account/login/workout/PT/admin flows before Play rollout.

Latest local backend result: 1,021/1,021 unit tests passed on 2026-10-26. Package uploaded to `/home/fitmate/releases/mobile-update-20261026`; archive SHA256 comparison and both remote bash syntax checks passed. Staging is not deployment. Isolated PostgreSQL/runtime/production rollout remains operator pending. No new mobile test/build/Play upload in this audit. Push, payments, model inference and billing are still open modules, not declared complete.
