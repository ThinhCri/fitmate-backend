# AI Plus chat-only launch — 29,000 VND prepaid month

Owner decision: 2026-11-08. Scope: chat and workout suggestions only. Food scan is not for sale. No deployment or real charge has been verified by this runbook.

## Play Console (operator)
1. Fitmate > Monetize with Play > Products > Subscriptions. Create/edit product `fitmate_ai_plus`, name `Fitmate AI Plus`.
2. Base plan `monthly`: prepaid, one calendar month, not auto-renewing. Vietnam price VND29,000. Confirm the final buyer-visible price, not an after-tax/net receipt target. Activate the base plan.
3. Benefits (each at most 40 characters): `100 lượt chat và gợi ý tập mỗi ngày`; `Trả trước một tháng, không tự gia hạn`. Do not advertise food scans, unlimited use or 30 fixed days.
4. Use the Fitmate Google Cloud project, enable Google Play Android Developer API. Create a dedicated server service account (e.g. fitmate-play-billing). In Play Console Users and permissions grant access to Fitmate, View financial data/orders/cancellation responses and Manage orders and subscriptions. Admin/publishing rights are unnecessary.
5. Download its JSON key securely to the operator machine, not the repo. Populate backend `GooglePlayBilling__ServiceAccountJsonBase64` without printing it. Do not send the key in chat. Add the purchasing test Gmail to account-level License testing.

## Deploy before opening sales (operator)
- Reviewed backend source includes `AiPlusSalesPolicy.cs`, billing options/controller and Program.cs. Existing `prepare-mobile-update.ps1` manifest and `deploy-mobile-update.sh` allowlist include these files. No new database schema change in this scope.
- Back up private .env and preserve DataProtection named volume. Keep `SalesEnabled=false` until native build5/internal testing setup is ready. Never disable verification to close sales for existing customers.
- Configuration after credentials are ready:

```dotenv
Ai__Enabled=true
Ai__CompanionChatEnabled=true
Ai__WorkoutAdjustEnabled=true
Ai__CompanionProvider=groq
Ai__FoodScanEnabled=false
# Keep the existing real Groq settings; never put keys in mobile.
AiUsage__Enabled=true
AiUsage__FreeChatPerDay=10
AiUsage__PlusChatPerDay=100
AiUsage__FreeScansPerDay=0
AiUsage__PlusScansPerDay=0
GooglePlayBilling__Enabled=true
GooglePlayBilling__SalesEnabled=false
GooglePlayBilling__PackageName=vn.fitmate.app
GooglePlayBilling__ProductId=fitmate_ai_plus
GooglePlayBilling__BasePlanId=monthly
GooglePlayBilling__MinimumAndroidVersionCode=5
# Supply ServiceAccountJsonBase64 privately. No placeholder value is deployable.
```

Enable `SalesEnabled=true` only once the real Play product is active, the key validates, UI build5+ is on the intended test track, and the real chat provider works. Old clients without build>=5 keep sales hidden, but restore/verified entitlement are retained. A native build header only gates storefront presentation, not purchase ownership or permissions.

## Android acceptance
- Prepared version0.1.4/code5. Agent does not start app/emulator or sign/upload an AAB. Owner uses `build-release-secure.ps1` and Play internal testing first; do not publish code5 to production until payment UAT.
- Play test purchase using license test card: verify success->Plus100, pending/decline->no Plus, restore after sign-in, wrong Fitmate account cannot reuse a purchase credential. Expiry, revocation or refund returns Free10. Verify actual reset at00:00 Asia/Ho_Chi_Minh. Concurrent requests from two devices cannot exceed quota; failed provider requests do not consume quota. Never report test charges as real revenue.
- Actual-charge check follows test success, using an appropriate non-license-tester Google account and explicit operator purchase action. Confirm Google order, receipt and server entitlement. Google pays to the configured bank on its payout schedule, not immediately. Admin fee/tax/payout reporting is a separate unfinished feature; do not fabricate net receipts from product price.
- Scan in a release app always shows Coming soon, opens no picker, makes no scan request, and offers manual entry. Local Kaggle demo only: debug app `.env` `EXPO_PUBLIC_FOOD_SCAN_DEMO=true`; configure an isolated local/demo backend endpoint and token and enable its scan capability/quota. Never enable the public VPS scan flag for a classroom demo.

Sources: https://support.google.com/googleplay/android-developer/answer/140504 and https://developers.google.com/android-publisher/getting_started . A Cloud project no longer needs a legacy Play account-linking step.
