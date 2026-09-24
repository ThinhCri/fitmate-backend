namespace Fitmate.Application.Payments;

public class VnpayOptions
{
    public const string SectionName = "Vnpay";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ApiUrl { get; set; } = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";
    public string ReturnUrl { get; set; } = "http://localhost:5000/api/v1/payments/vnpay/return";
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(TmnCode) &&
        !string.IsNullOrWhiteSpace(HashSecret) &&
        !string.IsNullOrWhiteSpace(PaymentUrl) &&
        !string.IsNullOrWhiteSpace(ApiUrl);
}

