using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Ai;
using Fitmate.Domain.Common;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Ai;

// Reuse the durable token/owner ledger; BasePlanId is empty for one-time purchases.
// Consumption is acknowledgement of delivery, not expiry/removal of entitlement.
public sealed class EfAiOneTimePurchaseService(FitmateDbContext db, IGooglePlayProductVerifier google,
    IOptions<GooglePlayBillingOptions> settings, IDataProtectionProvider protection) : IAiOneTimePurchaseService
{
    private readonly IDataProtector protector = protection.CreateProtector("fitmate-google-play-purchase-token-v1");

    public async Task VerifyAsync(Guid userId, string token, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new DomainException("AI_BILLING_UNAVAILABLE", "Chưa thể xác minh giao dịch.", 503);
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace))
            throw new DomainException("AI_PURCHASE_INVALID", "Mã giao dịch không hợp lệ.", 400);
        var purchase = await google.VerifyProductAsync(token, ct);
        var binding = Hash("fitmate-play:" + userId.ToString("N")).ToLowerInvariant();
        // New product always requires the Google-verified app-account identifier.
        // Missing identifiers are NOT assigned to whichever account presses Restore.
        if (purchase.ExternalAccountId != binding)
            throw new DomainException("AI_SUBSCRIPTION_BOUND_TO_ACCOUNT", "Giao dịch này không thuộc tài khoản Fitmate đang đăng nhập.", 409);
        var hash = Hash(token);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serialize different purchases on one user as well as replay of one token.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ai-user:" + userId}, 0))", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ai-sub:" + hash}, 0))", ct);
            var row = await db.AiSubscriptions.SingleOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
            if (row != null && row.UserId != userId) throw new DomainException("AI_SUBSCRIPTION_BOUND_TO_ACCOUNT", "Giao dịch đã thuộc tài khoản khác.", 409);
            if (row == null)
            {
                if (purchase.State != "PURCHASED") throw new DomainException("AI_PURCHASE_PENDING", "Giao dịch chưa được thanh toán thành công.", 409);
                if (purchase.Consumed) throw new DomainException("AI_PURCHASE_RECOVERY_REQUIRED", "Giao dịch đã xử lý nhưng chưa có hồ sơ quyền sử dụng. Vui lòng liên hệ hỗ trợ.", 409);
                var clock = DateTime.UtcNow;
                var now = new DateTime(clock.Ticks - clock.Ticks % 10, DateTimeKind.Utc);
                var end = await db.AiSubscriptions.Where(x => x.UserId == userId &&
                    x.State == "SUBSCRIPTION_STATE_ACTIVE" && x.ExpiresAtUtc > now)
                    .MaxAsync(x => x.ExpiresAtUtc, ct);
                row = new AiSubscriptionRecord
                {
                    Id = Guid.NewGuid(), UserId = userId, PurchaseTokenHash = hash,
                    ProtectedPurchaseToken = protector.Protect(token), CreatedAtUtc = now,
                    ProductId = purchase.ProductId, BasePlanId = string.Empty,
                    State = "SUBSCRIPTION_STATE_ACTIVE", ExpiresAtUtc = (end ?? now).AddMonths(1),
                    LatestOrderId = purchase.OrderId, VerifiedAtUtc = now, IsTestPurchase = purchase.IsTestPurchase
                };
                db.AiSubscriptions.Add(row);
                // Persist entitlement and retryable consumption atomically. No raw token in outbox.
                db.OutboxMessages.Add(OutboxMessage.Enqueue("ai.purchase.complete", JsonSerializer.Serialize(row.Id)));
            }
            else if (purchase.State == "CANCELLED")
            {
                row.State = "SUBSCRIPTION_STATE_REVOKED";
                row.VerifiedAtUtc = DateTime.UtcNow;
            }
            // Replay never adds another month and cannot reactivate a revoked grant.
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task ReconcileAsync(Guid recordId, CancellationToken ct)
    {
        var row = await db.AiSubscriptions.SingleAsync(x => x.Id == recordId, ct);
        if (row.ProductId != settings.Value.OneTimeProductId || row.BasePlanId.Length != 0)
            throw new DomainException("AI_PURCHASE_INVALID", "Loại giao dịch không hợp lệ.", 400);
        var token = protector.Unprotect(row.ProtectedPurchaseToken);
        var purchase = await google.VerifyProductAsync(token, ct);
        if (purchase.ExternalAccountId != Hash("fitmate-play:" + row.UserId.ToString("N")).ToLowerInvariant() ||
            purchase.OrderId != row.LatestOrderId) throw new DomainException("AI_PURCHASE_INVALID", "Không thể đối chiếu chủ giao dịch.", 409);
        if (purchase.State == "CANCELLED") row.State = "SUBSCRIPTION_STATE_REVOKED";
        row.VerifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Guid recordId, CancellationToken ct)
    {
        await ReconcileAsync(recordId, ct);
        var row = await db.AiSubscriptions.SingleAsync(x => x.Id == recordId, ct);
        if (row.State == "SUBSCRIPTION_STATE_REVOKED") return;
        await google.ConsumeAsync(protector.Unprotect(row.ProtectedPurchaseToken), ct);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class AiPurchaseCompleteHandler(IAiOneTimePurchaseService purchases) : IOutboxHandler
{
    public string Type => "ai.purchase.complete";
    public Task HandleAsync(string payloadJson, CancellationToken ct) =>
        purchases.CompleteAsync(JsonSerializer.Deserialize<Guid>(payloadJson), ct);
}
