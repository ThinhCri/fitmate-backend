using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;

namespace Fitmate.Infrastructure.Ai;

public sealed partial class GooglePlaySubscriptionVerifier
{
    public async Task<VerifiedGoogleProduct> VerifyProductAsync(string token, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw Unavailable();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.Value.PackageName)}/purchases/productsv2/tokens/{Uri.EscapeDataString(token)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(ct));
        using var response = await SendGoogleRequestAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            throw new DomainException("AI_PURCHASE_INVALID", "Google Play không xác nhận giao dịch này.", 400);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseProduct(document.RootElement, settings.Value.OneTimeProductId);
    }

    public static VerifiedGoogleProduct ParseProduct(JsonElement root, string productId)
    {
        if (!root.TryGetProperty("productLineItem", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() != 1 || ReadString(items[0], "productId") != productId ||
            !items[0].TryGetProperty("productOfferDetails", out var offer) ||
            offer.ValueKind != JsonValueKind.Object || offer.TryGetProperty("rentOfferDetails", out _) ||
            offer.TryGetProperty("preorderOfferDetails", out _) ||
            !offer.TryGetProperty("quantity", out var quantity) || !quantity.TryGetInt32(out var count) || count != 1 ||
            !root.TryGetProperty("purchaseStateContext", out var context)) throw InvalidProduct();
        var state = ReadString(context, "purchaseState");
        if (state is not ("PURCHASED" or "PENDING" or "CANCELLED")) throw InvalidProduct();
        var consumption = ReadString(offer, "consumptionState");
        if (consumption is not ("CONSUMPTION_STATE_CONSUMED" or "CONSUMPTION_STATE_YET_TO_BE_CONSUMED")) throw InvalidProduct();
        DateTime? purchased = DateTimeOffset.TryParse(ReadString(root, "purchaseCompletionTime"),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.UtcDateTime : null;
        var orderId = ReadString(root, "orderId") ?? string.Empty;
        if (state == "PURCHASED" && (purchased is null || string.IsNullOrWhiteSpace(orderId))) throw InvalidProduct();
        return new(productId, state, orderId, ReadString(root, "obfuscatedExternalAccountId"), purchased,
            consumption == "CONSUMPTION_STATE_CONSUMED",
            root.TryGetProperty("testPurchaseContext", out var test) && test.ValueKind == JsonValueKind.Object);
    }

    public async Task ConsumeAsync(string token, CancellationToken ct)
    {
        var purchase = await VerifyProductAsync(token, ct);
        if (purchase.Consumed || purchase.State == "CANCELLED") return;
        if (purchase.State != "PURCHASED") throw InvalidProduct();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.Value.PackageName)}/purchases/products/{Uri.EscapeDataString(settings.Value.OneTimeProductId)}/tokens/{Uri.EscapeDataString(token)}:consume");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(ct));
        using var response = await SendGoogleRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // A concurrent completion may already have consumed it. Check Google;
            // never treat a generic provider error as successful acknowledgement.
            if ((await VerifyProductAsync(token, ct)).Consumed) return;
            throw Unavailable();
        }
    }

    private VerifiedPlayOrder ParseSupportedOrder(JsonElement root, string orderId, string token)
    {
        var oneTime = root.TryGetProperty("lineItems", out var items) && items.ValueKind == JsonValueKind.Array &&
            items.GetArrayLength() == 1 && ReadString(items[0], "productId") == settings.Value.OneTimeProductId;
        return GooglePlayOrderParser.Parse(root, orderId, token,
            oneTime ? settings.Value.OneTimeProductId : settings.Value.ProductId,
            oneTime ? string.Empty : settings.Value.BasePlanId);
    }

    private static DomainException InvalidProduct() => new("AI_PURCHASE_INVALID",
        "Giao dịch không thuộc gói AI Plus mua một lần hợp lệ.", 400);
}
