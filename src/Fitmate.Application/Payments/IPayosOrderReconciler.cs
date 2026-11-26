namespace Fitmate.Application.Payments;

/// <summary>
/// Provides stored PayOS QR/transfer details and reconciles pending PayOS orders
/// directly from the authenticated provider API when callbacks are delayed or missed.
/// </summary>
public interface IPayosOrderReconciler
{
    Task<ProviderCreatePaymentResult?> GetStoredPaymentDetailsAsync(Guid paymentId, CancellationToken ct);
    Task ReconcileIfPaidAsync(Guid paymentId, CancellationToken ct);
    Task<bool> IsPackagePaymentOwnedByMemberAsync(Guid packageId, Guid memberId, CancellationToken ct);
}
