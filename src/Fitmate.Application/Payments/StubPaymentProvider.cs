using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public class StubPaymentProvider : IPaymentProvider
{
    public PaymentMethod Method => PaymentMethod.Vnpay;

    public Task<ProviderCreatePaymentResult> CreatePaymentAsync(
        ProviderCreatePaymentRequest request, CancellationToken ct)
    {
        var mockRef = $"mock-{request.PaymentId:N}";
        var mockUrl = $"https://mock.fitmate.local/payments/{request.PaymentId}/redirect";
        return Task.FromResult(new ProviderCreatePaymentResult(mockRef, mockUrl));
    }

    public Task<ProviderQueryResult> QueryTransactionAsync(
        ProviderQueryRequest request, CancellationToken ct)
    {
        return Task.FromResult(new ProviderQueryResult(
            IsSuccess: true,
            ResponseCode: "00",
            TransactionStatus: "00",
            ProviderTxnNo: $"mock-txn-{request.PaymentId:N}",
            Message: "Query success (stub)"));
    }

    public Task<ProviderRefundResult> RefundAsync(
        ProviderRefundRequest request, CancellationToken ct)
    {
        return Task.FromResult(new ProviderRefundResult(
            IsSuccess: true,
            ResponseCode: "00",
            ProviderRefundRef: $"mock-ref-{request.RefundId:N}",
            Message: "Refund success (stub)"));
    }
}
