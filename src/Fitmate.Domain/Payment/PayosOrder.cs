namespace Fitmate.Domain.Payment;

// Persisted before calling payOS. The database identity is the provider order code,
// so retries and concurrent requests cannot allocate a second external order.
public sealed class PayosOrder
{
    public long OrderCode { get; private set; }
    public Guid PaymentId { get; private set; }
    public string? PaymentLinkId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    private PayosOrder() { }
    public static PayosOrder Create(Guid paymentId, DateTime expiresAt) => new()
    { PaymentId = paymentId, ExpiresAt = expiresAt };
    public void BindLink(string linkId)
    {
        if (PaymentLinkId != null && PaymentLinkId != linkId)
            throw new InvalidOperationException("Provider payment link changed for the same order.");
        PaymentLinkId = linkId;
    }
}
