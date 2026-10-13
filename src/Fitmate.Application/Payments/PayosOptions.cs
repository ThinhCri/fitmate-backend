using System.Text.Json;

namespace Fitmate.Application.Payments;

public sealed class PayosOptions
{
    public const string SectionName = "Payos";
    public bool Enabled { get; set; }
    // Operator confirms the provider approved Fitmate's collection/settlement model.
    public bool MerchantApproved { get; set; }
    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChecksumKey { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";

    public bool CanVerifyWebhook() =>
        new[] { ClientId, ApiKey, ChecksumKey, AccountNumber }.All(s =>
            !string.IsNullOrWhiteSpace(s) && !s.Contains("__SET_", StringComparison.Ordinal));

    public bool IsValid() => MerchantApproved && CanVerifyWebhook() &&
        IsHttps(ReturnUrl) && IsHttps(CancelUrl);

    private static bool IsHttps(string value) => !string.IsNullOrWhiteSpace(value) && !value.Contains("__SET_", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
}

public interface IPayosWebhookHandler
{
    Task HandleAsync(JsonElement body, CancellationToken ct);
}

// Reserves the order in the same transaction as the payment, before external I/O.
public interface IPaymentReservation
{
    void Reserve(Guid paymentId, DateTime expiresAt);
}
