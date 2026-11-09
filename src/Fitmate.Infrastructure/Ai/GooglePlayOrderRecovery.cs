using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Ai;

// Explicit operator reconciliation, never an unrestricted purchase-token import endpoint.
public sealed class GooglePlayOrderRecovery(FitmateDbContext db, IGooglePlayOrderDiscovery discovery,
    IGooglePlaySubscriptionVerifier google, IAiSubscriptionService subscriptions, AiPlayOrderSync sync)
{
    public async Task RecoverAsync(string orderId, CancellationToken ct)
    {
        var token = await discovery.GetPurchaseTokenAsync(orderId, ct);
        var verified = await google.VerifyAsync(token, ct);
        if (verified.IsTestPurchase != false)
            throw new DomainException("AI_ORDER_NOT_REAL", "Không nhập giao dịch thử nghiệm hoặc chưa phân loại.", 409);
        var hash = Hash(token);
        var owner = await db.AiSubscriptions.AsNoTracking().Where(x => x.PurchaseTokenHash == hash)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        // Only a Google-verified predecessor chain proves ownership for legacy purchases.
        // A Play email, a supplied Fitmate email, or an arbitrary selected account does not.
        var linked = verified.LinkedPurchaseToken;
        var seen = new HashSet<string>(StringComparer.Ordinal) { hash };
        for (var depth = 0; owner == null && !string.IsNullOrWhiteSpace(linked) && depth < 10; depth++)
        {
            var linkedHash = Hash(linked);
            if (!seen.Add(linkedHash)) break;
            owner = await db.AiSubscriptions.AsNoTracking().Where(x => x.PurchaseTokenHash == linkedHash)
                .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
            if (owner == null) linked = (await google.VerifyAsync(linked, ct)).LinkedPurchaseToken;
        }
        if (owner == null && verified.ExternalAccountId is { Length: 64 } external)
        {
            var owners = await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM asp_net_users
                WHERE encode(sha256(convert_to('fitmate-play:' || replace("Id"::text, '-', ''), 'UTF8')), 'hex') = {external}
                """).Take(2).ToListAsync(ct);
            if (owners.Count == 1) owner = owners[0];
        }
        if (owner == null)
            throw new DomainException("AI_ORDER_OWNER_UNKNOWN", "Chưa xác định được chủ tài khoản; cần khôi phục từ đúng tài khoản đã mua.", 409);
        // Existing service enforces account binding and acknowledges only an active real purchase.
        await subscriptions.VerifyAsync(owner.Value, token, ct);
        var row = await db.AiSubscriptions.SingleAsync(x => x.PurchaseTokenHash == hash, ct);
        await sync.SyncAsync(row.Id, ct, orderId);
        await db.AiSubscriptions.Where(x => x.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderSyncAtUtc, DateTime.UtcNow), ct);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
