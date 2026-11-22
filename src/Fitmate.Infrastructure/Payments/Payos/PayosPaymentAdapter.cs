using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Payos;

public sealed class PayosPaymentAdapter(HttpClient http, FitmateDbContext db, IOptions<PayosOptions> options)
    : IPaymentProvider, IPaymentReservation
{
    private readonly PayosOptions _options = options.Value;
    public PaymentMethod Method => PaymentMethod.Payos;

    public void Reserve(Guid paymentId, DateTime expiresAt) => db.Set<PayosOrder>().Add(PayosOrder.Create(paymentId, expiresAt));

    public async Task<ProviderCreatePaymentResult> CreatePaymentAsync(ProviderCreatePaymentRequest request, CancellationToken ct)
    {
        EnsureEnabled();
        var order = await FindOrderAsync(request.PaymentId, ct);
        if (order.ExpiresAt <= DateTime.UtcNow)
            throw new ConflictException("HOLD_EXPIRED", "The payment hold has expired.");
        var code = order.OrderCode.ToString(CultureInfo.InvariantCulture);
        const string description = "FITMATE"; // Fits the strictest 9-character bank limit.
        var cancelUrl = BrowserReturnUrl(_options.CancelUrl, request.PaymentId);
        var returnUrl = BrowserReturnUrl(_options.ReturnUrl, request.PaymentId);
        var canonical = $"amount={request.AmountVnd.ToString(CultureInfo.InvariantCulture)}&cancelUrl={cancelUrl}&description={description}&orderCode={code}&returnUrl={returnUrl}";
        using var message = Request(HttpMethod.Post, "v2/payment-requests");
        message.Content = JsonContent.Create(new
        {
            orderCode = order.OrderCode, amount = request.AmountVnd, description,
            cancelUrl, returnUrl,
            signature = PayosSignature.Sign(canonical, _options.ChecksumKey)
        });
        using var response = await SendAsync(message, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.GetProperty("code").GetString() != "00")
        {
            // A retry can encounter an existing order after a lost response. Query the
            // SAME durable code; never create another order to work around an error.
            var existing = await QueryOrderAsync(order, ct);
            if (existing.GetProperty("amount").GetInt64() != request.AmountVnd ||
                existing.GetProperty("orderCode").GetInt64() != order.OrderCode)
                throw Unavailable();
            string? id = existing.TryGetProperty("paymentLinkId", out var plElem)
                ? plElem.GetString()
                : (existing.TryGetProperty("id", out var idElem) ? idElem.GetString() : null);
            if (string.IsNullOrWhiteSpace(id)) throw Unavailable();
            var normalizedId = id.Replace("-", "");
            if (!Guid.TryParse(id, out _) && !Guid.TryParseExact(normalizedId, "N", out _)) throw Unavailable();
            order.BindLink(normalizedId);
            string? qr = existing.TryGetProperty("qrCode", out var eqr) ? eqr.GetString() : null;
            string? acc = existing.TryGetProperty("accountNumber", out var eacc) ? eacc.GetString() : _options.AccountNumber;
            string? name = existing.TryGetProperty("accountName", out var ename) ? ename.GetString() : null;
            string? binCode = existing.TryGetProperty("bin", out var ebin) ? ebin.GetString() : null;
            string? d = existing.TryGetProperty("description", out var edesc) ? edesc.GetString() : description;
            string checkoutUrl = existing.TryGetProperty("checkoutUrl", out var coElem) && !string.IsNullOrWhiteSpace(coElem.GetString())
                ? coElem.GetString()!
                : "https://pay.payos.vn/web/" + id;
            return new ProviderCreatePaymentResult(code, checkoutUrl, qr, acc, name, binCode, d, order.OrderCode);
        }
        var data = root.GetProperty("data");
        if (!PayosSignature.Verify(data, root.GetProperty("signature").GetString(), _options.ChecksumKey) ||
            data.GetProperty("orderCode").GetInt64() != order.OrderCode ||
            data.GetProperty("amount").GetInt64() != request.AmountVnd ||
            data.GetProperty("currency").GetString() != "VND" ||
            !string.Equals(data.GetProperty("accountNumber").GetString()?.Replace(" ", ""), _options.AccountNumber?.Replace(" ", ""), StringComparison.Ordinal))
            throw Unavailable();
        var checkout = data.GetProperty("checkoutUrl").GetString()!;
        if (!Uri.TryCreate(checkout, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "pay.payos.vn" || !string.IsNullOrEmpty(uri.UserInfo)) throw Unavailable();
        order.BindLink(data.GetProperty("paymentLinkId").GetString()!);

        string? qrCode = data.TryGetProperty("qrCode", out var qrElem) ? qrElem.GetString() : null;
        string? accountNumber = data.TryGetProperty("accountNumber", out var accElem) ? accElem.GetString() : _options.AccountNumber;
        string? accountName = data.TryGetProperty("accountName", out var nameElem) ? nameElem.GetString() : null;
        string? bin = data.TryGetProperty("bin", out var binElem) ? binElem.GetString() : null;
        string? desc = data.TryGetProperty("description", out var descElem) ? descElem.GetString() : description;

        return new ProviderCreatePaymentResult(code, checkout, qrCode, accountNumber, accountName, bin, desc, order.OrderCode);
    }

    public async Task<ProviderQueryResult> QueryTransactionAsync(ProviderQueryRequest request, CancellationToken ct)
    {
        EnsureEnabled();
        var order = await FindOrderAsync(request.PaymentId, ct);
        var data = await QueryOrderAsync(order, ct);
        var expectedAmount = await db.Payments.Where(p => p.Id == request.PaymentId).Select(p => p.AmountVnd).SingleAsync(ct);
        if (data.GetProperty("amount").GetInt64() != expectedAmount) throw Unavailable();
        var status = data.GetProperty("status").GetString()!;
        string? txnId = data.TryGetProperty("paymentLinkId", out var plId) ? plId.GetString() : (data.TryGetProperty("id", out var tid) ? tid.GetString() : null);
        return new ProviderQueryResult(status == "PAID", "00", status, txnId,
            "Provider status only. Settlement requires a verified webhook.");
    }

    public Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken ct) =>
        Task.FromResult(new ProviderRefundResult(false, "MANUAL_REFUND_REQUIRED", null,
            "No automatic refund or payout adapter is enabled."));

    private Task<PayosOrder> FindOrderAsync(Guid paymentId, CancellationToken ct) =>
        db.Set<PayosOrder>().SingleAsync(o => o.PaymentId == paymentId, ct);

    private static string BrowserReturnUrl(string configuredUrl, Guid paymentId)
    {
        var uri = new UriBuilder(configuredUrl);
        var existingQuery = uri.Query.TrimStart('?');
        uri.Query = (existingQuery.Length == 0 ? "" : existingQuery + "&") +
            "paymentId=" + paymentId.ToString("D");
        return uri.Uri.AbsoluteUri;
    }

    private async Task<JsonElement> QueryOrderAsync(PayosOrder order, CancellationToken ct)
    {
        using var message = Request(HttpMethod.Get, "v2/payment-requests/" + order.OrderCode.ToString(CultureInfo.InvariantCulture));
        using var response = await SendAsync(message, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.GetProperty("code").GetString() != "00") throw Unavailable();
        var data = doc.RootElement.GetProperty("data");
        if (data.GetProperty("orderCode").GetInt64() != order.OrderCode) throw Unavailable();
        return data.Clone(); // Authenticated HTTPS query; some query versions omit signature.
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, "https://api-merchant.payos.vn/" + path);
        message.Headers.Add("x-client-id", _options.ClientId);
        message.Headers.Add("x-api-key", _options.ApiKey);
        return message;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        try
        {
            var response = await http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode) return response;
            response.Dispose();
            throw Unavailable();
        }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private void EnsureEnabled()
    {
        if (!_options.Enabled || !_options.IsValid()) throw Unavailable();
    }
    private static DomainException Unavailable() => new("PAYMENT_PROVIDER_UNAVAILABLE", "payOS is unavailable or returned an invalid response.", 503);
}
