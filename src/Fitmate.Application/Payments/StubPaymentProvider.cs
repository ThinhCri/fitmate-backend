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
}
