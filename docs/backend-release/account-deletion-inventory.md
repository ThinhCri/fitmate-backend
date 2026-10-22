# Account deletion data inventory (2026-10-22)

This is an implementation inventory, not an approval to run deletion on production.
Owner chose to retain only minimal anonymized financial and audit evidence. The
The 30-day clock is opt-in through `AccountDeletion__FinalizerEnabled`; it remains
off by default. Do not enable it on a production dataset until the backup replay
and retention-policy gates below are resolved.

| Category | Primary tables / external copies | Intended action after 30 days |
|---|---|---|
| Identity/session | `asp_net_users`, user claims/logins/tokens/roles, `auth_sessions`, refresh tokens, devices, profile/preferences | Tombstone user UUID; clear email, phone, password, security identifiers and profile/health data; revoke/delete sessions. Email/phone become reusable only after this transaction commits. |
| Fitness/health/AI | workout/weight/nutrition tables, AI usage/subscription records | Delete health, routine and usage rows. Keep the minimal subscription purchase record while clearing its protected purchase token. |
| Community/chat | posts, comments, reactions, friendships, reports, chat, reviews | Remove owned content/relationships and redact user-authored content retained in shared threads; do not erase other users' content without a policy decision. |
| PT/onboarding | PT profile, certificates, private scans, payout account, services, availability | Delete private credentials/bank details/media and availability; retain only booking-linked service facts needed for financial evidence, with free text scrubbed. |
| Booking/payment | appointments, package/holds, payment/refund/payos, ledger, manual settlement | Preserve amounts, timestamps, status, reconciliation IDs; clear free-text notes, banking personally identifying fields and private evidence not legally required. Block deletion while money or disputes are unsettled. |
| Support/notifications | support tickets/messages/attachments, notifications, push deliveries, outbox, idempotency | Delete or redact user-authored text and destinations; preserve only minimal non-PII audit of processed actions. |
| Media/cache/backup | local/R2 object keys, signed URL caches, DB/media backups | Enumerate uploader-owned objects, delete them before committing the account tombstone, and retry from the pending user row if deletion fails. Backups require tombstone replay before traffic after restore and an expiry policy. |

Implemented and tested against isolated PostgreSQL: migration chain, email/phone
reservation and reuse, finalization/redaction of representative booking, payment,
PT certificate, social/chat, health and media data, storage-failure rollback,
unsettled-appointment guard, and admin/finalizer row-lock serialization. The
pending user row is the durable retry marker; a failed storage delete never
marks the account Deleted. The worker scans due rows in bounded batches.

Remaining production gates: rehearse a DB **and media** restore; implement and
verify replay of deletion tombstones against restored backups before opening
traffic; approve financial/audit retention periods and any legal holds; confirm
legacy media keys can be attributed to the right uploader; run an operator-
supervised closed-test deletion with real deployment configuration. Until then,
keep `AccountDeletion__FinalizerEnabled=false`. A retained user UUID and provider
reconciliation references are pseudonymized, not guaranteed anonymous.
