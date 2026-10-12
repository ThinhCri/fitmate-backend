using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed record AdminTransaction(
    Guid Id, string Type, string Status, long AmountVnd, DateTime OccurredAtUtc,
    Guid? PaymentId, Guid? AppointmentId, Guid? PackageId, string? ProviderReference);

public sealed record AdminTransactionPage(
    IReadOnlyList<AdminTransaction> Items, int Total, int Page, int PageSize);

public interface IAdminTransactionsStore
{
    Task<AdminTransactionPage> GetAsync(string? type, string? status, Guid? referenceId,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct);
}

public sealed class AdminTransactionsService(IAdminTransactionsStore store, IAuthorizationGuard authz)
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
        { "payment", "refund", "payout" };
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
        { "pending", "paid", "failed", "refunded", "partial_refunded", "succeeded" };

    public async Task<AdminTransactionPage> GetAsync(Guid adminId, string? type, string? status,
        Guid? referenceId, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        type = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if ((type != null && !Types.Contains(type)) || (status != null && !Statuses.Contains(status)) ||
            page < 1 || pageSize is < 1 or > 100 ||
            (fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) ||
            (toUtc.HasValue && toUtc.Value.Kind != DateTimeKind.Utc) ||
            (fromUtc.HasValue && toUtc.HasValue && fromUtc >= toUtc))
            throw new DomainException("TRANSACTION_FILTER_INVALID", "Check transaction type, status, UTC range and pagination.", 400);
        return await store.GetAsync(type, status, referenceId, fromUtc, toUtc, page, pageSize, ct);
    }
}
