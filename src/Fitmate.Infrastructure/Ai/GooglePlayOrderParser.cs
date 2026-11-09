using System.Globalization;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;

namespace Fitmate.Infrastructure.Ai;

public static class GooglePlayOrderParser
{
    public static VerifiedPlayOrder Parse(JsonElement root, string orderId, string token, string productId, string basePlanId = "monthly")
    {
        if (Text(root, "orderId") != orderId || Text(root, "purchaseToken") != token ||
            !root.TryGetProperty("lineItems", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() != 1 || Text(items[0], "productId") != productId ||
            !MatchesType(items[0], basePlanId))
            throw Invalid();
        var state = Text(root, "state");
        if (state is not ("PROCESSED" or "PENDING" or "CANCELED" or "PENDING_REFUND" or "PARTIALLY_REFUNDED" or "REFUNDED")) throw Invalid();
        if (!DateTimeOffset.TryParse(Text(root, "createTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var created)) throw Invalid();
        var total = Money(root, "total") ?? throw Invalid();
        if (total < 0) throw Invalid();
        return new(orderId, state, created.UtcDateTime, total, Money(root, "tax"), Money(root, "developerRevenueInBuyerCurrency"));
    }

    private static bool MatchesType(JsonElement item, string basePlanId) => basePlanId.Length == 0
        ? item.TryGetProperty("oneTimePurchaseDetails", out var oneTime) && oneTime.ValueKind == JsonValueKind.Object &&
          !item.TryGetProperty("subscriptionDetails", out _)
        : item.TryGetProperty("subscriptionDetails", out var details) && Text(details, "basePlanId") == basePlanId;

    private static long? Money(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (Text(value, "currencyCode") != "VND") throw Invalid();
        var unitsText = Text(value, "units") ?? "0";
        if (!long.TryParse(unitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var units)) throw Invalid();
        var nanos = value.TryGetProperty("nanos", out var n) ? n.GetInt32() : 0;
        if (nanos <= -1000000000 || nanos >= 1000000000 || units > 0 && nanos < 0 || units < 0 && nanos > 0) throw Invalid();
        // Google Money uses integers; round half-up to the nearest dong, never float.
        return checked(units + (nanos >= 500000000 ? 1 : nanos <= -500000000 ? -1 : 0));
    }
    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static DomainException Invalid() => new("AI_ORDER_INVALID", "Không thể đối chiếu thông tin đơn hàng Google Play.", 503);
}
