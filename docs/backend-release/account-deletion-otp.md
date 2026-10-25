# Authenticated account-deletion OTP

POST `/api/v1/users/me/deletion-otp` queues an email OTP for the authenticated account. It does not submit deletion. Response: `expires_at`, `resend_after_seconds` (60). OTP expires after five minutes; at most five wrong attempts per challenge. Resending invalidates the previous challenge. Codes and recipient are protected in the outbox; no plaintext code is persisted or logged by this implementation.

POST `/api/v1/users/me/delete-request` and legacy DELETE `/api/v1/users/me` require `{ "otp": "<six digits>", "reason": "optional" }`. Missing/malformed code returns 400, wrong/expired/used/exhausted code returns 400; inactive/no-email account 403; pending obligations 409. Sending again too early returns 429. Existing pending request prevents another OTP start with 409. User row lock serializes send/verify; verified code is consumed in the same transaction as request creation. A blocked deletion rolls back consumption so a still-valid code can be retried after resolving the blocker.

The request still awaits admin approval; the retention clock is not started by OTP verification. Public email-link deletion is unchanged. No migration or user-volume operations are needed.

Rollout: deploy backend and update Android app deliberately. Previous mobile versions do not supply OTP and cannot submit authenticated deletion afterward; public email-link flow remains an alternative. No production SMTP, real deletion or rollout is tested/authorized here.

Checks: focused service unit tests; isolated PostgreSQL gateway tests for success/replay, persisted five failures, expiry, email/account binding, resend cooldown and anonymous endpoints. Device email receipt, keyboard positioning and permission navigation require operator UAT. Duplicate notification delivery has not been reproduced; this task removes confusing duplicate controls, not an asserted provider-delivery bug.

Local result: service/email unit tests 20/20 passed; mobile account tests 3/3, TypeScript and targeted ESLint passed. Integration project compiles, but all seven focused integration cases were blocked at fixture startup: local PostgreSQL unavailable and Docker Desktop Linux engine not running. They have not exercised the gateway against a database yet. Start the isolated local test infrastructure and rerun `dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter FullyQualifiedName~AccountDeletionOtpTests` before rollout.
