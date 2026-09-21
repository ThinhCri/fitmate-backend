namespace Fitmate.Application.Payments;

public interface IPaymentService
{
    Task<PaymentView> CreateAsync(Guid memberId, CreatePaymentRequest request, CancellationToken ct);
    Task<PaymentView?> GetByIdAsync(Guid memberId, Guid paymentId, CancellationToken ct);
    object HandleVnpayReturn(VnpayReturnQuery query);
    object HandleVnpayIpn(VnpayReturnQuery query);
}
