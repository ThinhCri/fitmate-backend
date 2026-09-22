using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public class MissingPaymentProvider : IPaymentProvider
{
    private readonly string _reason;

    public MissingPaymentProvider(string reason = "Payment provider is not configured.")
    {
        _reason = reason;
    }

    public PaymentMethod Method => PaymentMethod.Vnpay;

    public Task<ProviderCreatePaymentResult> CreatePaymentAsync(
        ProviderCreatePaymentRequest request, CancellationToken ct)
    {
        throw new DomainException(ErrorCodes.Payment.ProviderUnavailable, _reason, 503);
    }
}

