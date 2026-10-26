using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public interface IPaymentProvider
{
    bool IsRealProvider => true;
    PaymentMethod Method { get; }

    Task<ProviderCreatePaymentResult> CreatePaymentAsync(
        ProviderCreatePaymentRequest request,
        CancellationToken ct);

    Task<ProviderQueryResult> QueryTransactionAsync(
        ProviderQueryRequest request,
        CancellationToken ct);

    Task<ProviderRefundResult> RefundAsync(
        ProviderRefundRequest request,
        CancellationToken ct);
}

