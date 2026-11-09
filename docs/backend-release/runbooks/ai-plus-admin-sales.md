# Real AI Plus orders in admin

## Source and scope
Google subscriptionsv2 testPurchase classifies a token; only server-confirmed false is reportable. Orders GET supplies state, total, tax, developerRevenueInBuyerCurrency. Product/base plan, order ID and purchase token must match. VND only; no catalog fallback or inferred fee. Sources: https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.subscriptionsv2 and https://developers.google.com/android-publisher/api-ref/rest/v3/orders .

New worker uses existing protected subscription tokens and existing Play credentials. Backfills legacy unknown classifications; no new keys or paid resources. At most10 subscription attempts per minute, at least15 minutes between attempts per subscription. Separate accounting reads never block purchase entitlement activation. Test purchases never create an order row and unknown/test rows never enter API totals. Old tokens no longer accepted by subscriptionsv2 can still refresh already classified real order details.

## Reporting semantics
Admin > Finance > AI Plus orders. Filters by Vietnam calendar days, exact GPA order, state; deterministic server sort and pagination. Processed gross excludes refund states; Google supplied net includes processed/partial/refunded orders. If any net value is absent, aggregate net remains null. Order tax is not an individual's complete tax liability. Values are not bank payouts. Payout evidence remains Google Payments/earnings reports, not inferred from an active Plus subscription.

Scope is stored Fitmate purchase tokens, not all historical Play Console orders: Orders API needs known order IDs. Purchases never verified by Fitmate or aged-out historical tokens with no known order may require operator follow-up.

## Deployment
Prepare source with scripts/prepare-mobile-update.ps1. Stage admin production dist as admin.tgz under /home/fitmate/releases/ai-sales-20261108 alongside scripts/deploy-ai-sales.sh. Operator runs sudo bash /home/fitmate/releases/ai-sales-20261108/deploy-ai-sales.sh . It runs unit + isolated PostgreSQL integration tests, backs up prod, previews additive migration on restored private DB, deploys API then admin. No .env replacement, purchase initiation, refund or bank transfer. PostgreSQL volume retained. Roll back app image/static assets using private backups; additive reporting schema retained. Down requires empty reporting table and backup review.

## Acceptance
- Anonymous401/member403, invalid page/sort400, empty valid search200.
- Compare real GPA order/actual price with Play Console; test/unknown must not appear.
- Repeated restore/sync remains one row; Google refunds update row/state/net.
- No native/mobile rebuild required for this reporting-only change.
