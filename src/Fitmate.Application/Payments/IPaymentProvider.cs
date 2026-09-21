using Fitmate.Domain.Common;
using Fitmate.Domain.Payments;

namespace Fitmate.Application.Payments;

/// <summary>
/// Payment provider abstraction. Per DEC-027/YAGNI: chỉ định nghĩa interface,
/// không viết nhiều adapter giả. Stub <see cref="StubPaymentProvider"/> dùng cho
/// dev/verify; real <c>VnpayPaymentProvider</c> sẽ thay thế ở BE-400 (chờ UA-008).
/// </summary>
public interface IPaymentProvider
{
    PaymentMethod Method { get; }
    Task<ProviderCreatePaymentResult> CreatePaymentAsync(
        ProviderCreatePaymentRequest request,
        CancellationToken ct);
}
