#!/usr/bin/env bash
# Read-only aggregate diagnosis. No customer identity, purchase tokens or keys are printed.
set -Eeuo pipefail
set +x
umask 077
test "$(id -u)" = 0 || { echo 'Run with sudo bash.' >&2; exit 1; }
backend=/home/fitmate/backend
container=$(cd "$backend" && docker compose -f compose.vps.yml ps -q postgres)
test -n "$container"
echo '=== REPORT HEALTH ==='
curl --max-time 10 -fsS -o /dev/null -w 'API_HTTP=%{http_code}\n' http://127.0.0.1:8080/health/ready
printf 'API_STARTED_AT='
docker inspect --format '{{.State.StartedAt}}' fitmate-prod-api-1
# psql credentials are resolved inside the existing DB container, never returned.
docker exec -i "$container" sh -ec 'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
BEGIN TRANSACTION READ ONLY;
SELECT count(*) AS stored_subscriptions,
 count(*) FILTER (WHERE "IsTestPurchase" = false) AS real_subscriptions,
 count(*) FILTER (WHERE "IsTestPurchase" = true) AS test_subscriptions,
 count(*) FILTER (WHERE "IsTestPurchase" IS NULL) AS unclassified_subscriptions,
 count(*) FILTER (WHERE "IsTestPurchase" = false AND "LatestOrderId" IS NULL) AS real_without_order_id,
 count(*) FILTER (WHERE "OrderSyncAtUtc" IS NULL) AS never_attempted,
 count(*) FILTER (WHERE "OrderSyncAtUtc" < now() - interval '20 minutes') AS sync_attempt_older_than_20min
FROM ai_subscriptions;
SELECT count(*) AS real_subscriptions_missing_latest_order
FROM ai_subscriptions s
WHERE s."IsTestPurchase" = false AND s."LatestOrderId" IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM ai_play_orders o WHERE o."OrderId" = s."LatestOrderId");
SELECT o."State",count(*) AS real_orders,
 count(*) FILTER (WHERE o."DeveloperRevenueVnd" IS NULL) AS missing_net,
 count(*) FILTER (WHERE o."PurchasedAtUtc" >= ((now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 29) AT TIME ZONE 'Asia/Ho_Chi_Minh') AS orders_last_30_vn_days,
 max(o."SyncedAtUtc") AS last_successful_sync_utc
FROM ai_play_orders o JOIN ai_subscriptions s ON s."Id"=o."SubscriptionId"
WHERE s."IsTestPurchase" = false GROUP BY o."State" ORDER BY o."State";
-- Targeted ownership/status check for the two accounts reported by the operator.
-- Output labels only; no email, account IDs, purchase token or GPA is disclosed.
SELECT CASE lower(u."Email") WHEN 'phatno13252@gmail.com' THEN 'ACCOUNT_A' ELSE 'ACCOUNT_B' END AS account_label,
 s."State", s."ExpiresAtUtc", s."VerifiedAtUtc", s."OrderSyncAtUtc",
 (s."IsTestPurchase" = true) AS is_test,
 (s."LatestOrderId" IS NOT NULL) AS has_google_order_id,
 (o."OrderId" IS NOT NULL) AS has_reporting_order,
 o."State" AS reporting_state
FROM asp_net_users u
LEFT JOIN ai_subscriptions s ON s."UserId" = u."Id"
LEFT JOIN ai_play_orders o ON o."OrderId" = s."LatestOrderId"
WHERE lower(u."Email") IN ('phatno13252@gmail.com', 'phattan13252@gmail.com')
ORDER BY account_label, s."VerifiedAtUtc" DESC;
COMMIT;
SQL
echo '=== SAFE SYNC FAILURE COUNTS (last 2 hours) ==='
# Only match the deliberately credential-free worker message, not raw application logs.
docker logs --since 2h fitmate-prod-api-1 2>&1 | grep -oE 'AI order synchronization (pass )?failed: [A-Za-z0-9_.]+' | sort | uniq -c || true
echo 'AI_SALES_READONLY_DIAG_COMPLETE'
