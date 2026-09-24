namespace Fitmate.Application.Payments;

public interface IPaymentService
{
    Task<PaymentView> CreateAsync(Guid memberId, CreatePaymentRequest request, CancellationToken ct);
    Task<PaymentView?> GetByIdAsync(Guid memberId, Guid paymentId, CancellationToken ct);
    object HandleVnpayReturn(VnpayReturnQuery query);
    object HandleVnpayIpn(VnpayReturnQuery query);
    Task<VnpayIpnResponse> HandleVnpayIpnAsync(IDictionary<string, string> queryParams, CancellationToken ct);
    Task<ProviderQueryResult> QueryTransactionAsync(Guid paymentId, CancellationToken ct);
    Task<ProviderRefundResult> RefundAsync(Guid paymentId, long amountVnd, string reason, Guid adminId, CancellationToken ct);
}
