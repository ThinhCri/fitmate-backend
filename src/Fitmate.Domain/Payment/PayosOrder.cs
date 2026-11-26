namespace Fitmate.Domain.Payment;

// Persisted before calling payOS. The database identity is the provider order code,
// so retries and concurrent requests cannot allocate a second external order.
public sealed class PayosOrder
{
    public long OrderCode { get; private set; }
    public Guid PaymentId { get; private set; }
    public string? PaymentLinkId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string? QrCode { get; private set; }
    public string? AccountNumber { get; private set; }
    public string? AccountName { get; private set; }
    public string? Bin { get; private set; }
    public string? Description { get; private set; }
    private PayosOrder() { }
    public static PayosOrder Create(Guid paymentId, DateTime expiresAt) => new()
    { PaymentId = paymentId, ExpiresAt = expiresAt };
    public void BindLink(string linkId)
    {
        var normalized = linkId.Replace("-", "");
        if (PaymentLinkId != null && PaymentLinkId.Replace("-", "") != normalized)
            throw new InvalidOperationException("Provider payment link changed for the same order.");
        PaymentLinkId = normalized;
    }

    public void BindDetails(string? qrCode, string? accountNumber, string? accountName, string? bin, string? description)
    {
        if (!string.IsNullOrWhiteSpace(qrCode)) QrCode = qrCode;
        if (!string.IsNullOrWhiteSpace(accountNumber)) AccountNumber = accountNumber;
        if (!string.IsNullOrWhiteSpace(accountName)) AccountName = accountName;
        if (!string.IsNullOrWhiteSpace(bin)) Bin = bin;
        if (!string.IsNullOrWhiteSpace(description)) Description = description;
    }
}
