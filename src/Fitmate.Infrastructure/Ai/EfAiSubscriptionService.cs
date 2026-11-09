using System.Data;
using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Ai;
using Fitmate.Domain.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitmate.Infrastructure.Ai;

public sealed class EfAiSubscriptionService : IAiSubscriptionService
{
    private readonly FitmateDbContext _db;
    private readonly IGooglePlaySubscriptionVerifier _google;
    private readonly GooglePlayBillingOptions _options;
    private readonly IDataProtector _protector;

    public EfAiSubscriptionService(FitmateDbContext db, IGooglePlaySubscriptionVerifier google,
        IOptions<GooglePlayBillingOptions> options, IDataProtectionProvider protection)
    {
        _db = db;
        _google = google;
        _options = options.Value;
        _protector = protection.CreateProtector("fitmate-google-play-purchase-token-v1");
    }

    public async Task<AiSubscriptionView> GetAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        var current = await CurrentAsync(userId, ct);
        if (current is not null && _options.Enabled &&
            current.State is not ("SUBSCRIPTION_STATE_REVOKED" or "SUBSCRIPTION_STATE_REPLACED") &&
            (current.ExpiresAtUtc is null || current.ExpiresAtUtc.Value > DateTime.UtcNow.AddDays(-60)) &&
            current.VerifiedAtUtc.AddMinutes(Math.Clamp(_options.RefreshMinutes, 1, 60)) <= DateTime.UtcNow)
        {
            string token;
            try { token = _protector.Unprotect(current.ProtectedPurchaseToken); }
            catch (CryptographicException)
            {
                throw new DomainException("AI_BILLING_TOKEN_UNREADABLE",
                    "Không thể kiểm tra gói AI. Vui lòng liên hệ hỗ trợ.", 503);
            }
            // Google is the authority for prepaid expiry, revocation and refunds; no client-side flag is trusted.
            try { await VerifyAsync(userId, token, ct); }
            catch (DomainException ex) when (ex.Code == "AI_SUBSCRIPTION_INVALID")
            {
                // A token that Google no longer recognizes cannot retain a paid entitlement.
                await _db.AiSubscriptions.Where(x => x.Id == current.Id && x.UserId == userId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.State, "SUBSCRIPTION_STATE_REVOKED")
                        .SetProperty(x => x.ExpiresAtUtc, (DateTime?)null)
                        .SetProperty(x => x.VerifiedAtUtc, DateTime.UtcNow), ct);
            }
            current = await CurrentAsync(userId, ct);
        }
        return ToView(current, userId);
    }

    private Task<AiSubscriptionRecord?> CurrentAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // A background recheck of an older expired/replaced token must not hide
        // a still-valid purchase on the same account. Ownership remains per token.
        return _db.AiSubscriptions.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.State == "SUBSCRIPTION_STATE_ACTIVE" && x.ExpiresAtUtc > now)
            .ThenByDescending(x => x.ExpiresAtUtc)
            .ThenByDescending(x => x.VerifiedAtUtc).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AiSubscriptionView> VerifyAsync(Guid userId, string purchaseToken, CancellationToken ct)
    {
        if (!_options.Enabled)
            throw new DomainException("AI_BILLING_UNAVAILABLE", "Gói AI Plus chưa mở bán.", 503);
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(purchaseToken) ||
            purchaseToken.Length > 4096 || purchaseToken.Any(char.IsWhiteSpace))
            throw new DomainException("AI_SUBSCRIPTION_INVALID", "Mã giao dịch không hợp lệ.", 400);
        var hash = Hash(purchaseToken);
        var verified = await _google.VerifyAsync(purchaseToken, ct);
        if (!string.IsNullOrEmpty(verified.ExternalAccountId) &&
            !string.Equals(verified.ExternalAccountId, BillingAccountId(userId), StringComparison.Ordinal))
            throw new DomainException("AI_SUBSCRIPTION_BOUND_TO_ACCOUNT",
                "Giao dịch này được tạo cho tài khoản Fitmate khác. Hãy đăng nhập tài khoản đã mua.", 409);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using (var command = _db.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
                command.Parameters.Add(new NpgsqlParameter("key", $"ai-sub:{hash}"));
                await command.ExecuteScalarAsync(ct);
            }
            var existing = await _db.AiSubscriptions.FirstOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
            if (existing is not null && existing.UserId != userId)
                throw new DomainException("AI_SUBSCRIPTION_BOUND_TO_ACCOUNT",
                    "Giao dịch này đã gắn với tài khoản Fitmate khác.", 409);
            if (!string.IsNullOrWhiteSpace(verified.LinkedPurchaseToken))
            {
                var linkedHash = Hash(verified.LinkedPurchaseToken);
                var linked = await _db.AiSubscriptions.FirstOrDefaultAsync(x => x.PurchaseTokenHash == linkedHash, ct);
                if (linked is not null && linked.UserId != userId)
                    throw new DomainException("AI_SUBSCRIPTION_BOUND_TO_ACCOUNT",
                        "Giao dịch gia hạn đã gắn với tài khoản khác.", 409);
                if (linked is not null) linked.State = "SUBSCRIPTION_STATE_REPLACED";
            }
            // Do not acknowledge a token already bound to another Fitmate account.
            if (!verified.Acknowledged && verified.State == "SUBSCRIPTION_STATE_ACTIVE")
                await _google.AcknowledgeAsync(purchaseToken, ct);
            var now = DateTime.UtcNow;
            existing ??= new AiSubscriptionRecord
            {
                Id = Guid.NewGuid(), UserId = userId, PurchaseTokenHash = hash,
                ProtectedPurchaseToken = _protector.Protect(purchaseToken), CreatedAtUtc = now
            };
            if (_db.Entry(existing).State == EntityState.Detached) _db.AiSubscriptions.Add(existing);
            existing.ProductId = verified.ProductId;
            existing.BasePlanId = verified.BasePlanId;
            existing.State = verified.State;
            existing.ExpiresAtUtc = verified.ExpiresAtUtc;
            existing.VerifiedAtUtc = now;
            existing.IsTestPurchase = verified.IsTestPurchase;
            existing.LatestOrderId = verified.OrderId ?? existing.LatestOrderId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ToView(existing, userId);
        });
    }

    private AiSubscriptionView ToView(AiSubscriptionRecord? row, Guid userId)
    {
        var entitled = row is not null && row.ExpiresAtUtc > DateTime.UtcNow &&
            row.State == "SUBSCRIPTION_STATE_ACTIVE";
        return new AiSubscriptionView(entitled ? "plus" : "free", _options.Enabled && _options.SalesEnabled,
            _options.Enabled,
            _options.ProductId, _options.BasePlanId, row?.State ?? "none", row?.ExpiresAtUtc, BillingAccountId(userId));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string BillingAccountId(Guid userId) => Hash("fitmate-play:" + userId.ToString("N")).ToLowerInvariant();
}
