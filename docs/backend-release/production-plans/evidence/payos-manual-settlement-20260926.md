# payOS / manual PT settlement — implementation handoff

Date: 2026-09-26. Related tasks: BE-400/808/809/810, BE-801 contract, BE-803 MFA.
**Status: CODE_PREPARED_NOT_RUNTIME_VERIFIED.** No commit, package download, build, test, migration, Docker or live payment was run by the agent.

## User-supplied verification update — 2026-09-26
- Supplied terminal log: solution build succeeded with 72 warnings; filtered email integration tests passed 4/4 and email unit tests passed 24/24. Full unit suite passed 626/627.
- Failure: backlog guard included a fully refunded booking in recognized gross/fee (expected 700,000/70,000 rather than 500,000/50,000). Corrected the fixture expectations and added paid-but-unfinished exclusion and no-automatic-payout assertions.
- Post-fix test execution remains pending. Agent did not execute tests. Existing dependency vulnerability warnings are unresolved. No evidence here establishes PostgreSQL migration, payOS integration or production readiness.

## Implemented in source
- payOS HTTP create/query, payment-request HMAC validation, durable numeric order reservation, retry recovery using same provider code, late payment refund intent; no provider payout API.
- Payment reservation saved before external HTTP; provider/method isolation; no automatic development stub fallback.
- Verified manual settlement per appointment: both participant confirmation times, session/payee evidence references, review/hold, immutable transfer receipt, current amount/refund guard, cross-refund/payout bank-reference locking.
- Application authorization + mandatory finance MFA step-up for mutations; protected admin provider status endpoint.
- Manual refund intents remain Pending until actual transfer evidence. Legacy settlement shortcut and legacy payOS refund shortcut blocked.
- PT projection and admin payout ledger include recorded transfers. Idempotency cache scoped by authenticated actor/method/path; webhook always verifies its own signature.
- Migration with protected Down and explicit snapshot/designer, OpenAPI updates, runbook and two FE documentation addenda.

## Test sources (not executed)
- `PayosAndManualSettlementTests`: official public signature vector/tamper, config gate, contract serialization, no transfer without approval, both confirmations, duplicate receipt, refund rounding, role and MFA.
- `PayosPostgresIntegrationTests`: duplicate webhook concurrency, late payment, amount/account/signature rejection, duplicate transfer concurrency, pending refund hold and manual confirmation, fake HTTP checkout retry without a second order or real network.
- Existing idempotency tests adjusted for actor scope and added cross-actor/path/webhook coverage; old settlement test now asserts rejected shortcut/no outbox.

## Operator verification still required
Commands and prerequisites: [runbook](../../runbooks/payos-manual-settlement.md).
Record actual SDK/runtime, commands, results, PostgreSQL version/isolated DB, migration model check, HTTP auth/error cases, and manual UAT results here after running.
No test count or PASS claim is inferred from source review. Migration was authored without executing EF tools.
Merchant eligibility/credentials and explicit provider approval of the Fitmate multi-PT collection model remain OPS-03. Approval of an individual account alone does not approve this business model.
The existing larger production gates (evidence storage ownership, complete immutable accounting/adjustment ledger, frontend UAT and DR rehearsal) remain separate; this change is not production release approval.
