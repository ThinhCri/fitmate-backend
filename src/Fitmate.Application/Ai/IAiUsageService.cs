using System.Text.Json.Serialization;

namespace Fitmate.Application.Ai;

public sealed record AiUsageItemView(
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("used")] int Used,
    [property: JsonPropertyName("remaining")] int Remaining);

public sealed record AiUsageView(
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("enforcement_enabled")] bool EnforcementEnabled,
    [property: JsonPropertyName("resets_at_utc")] DateTime ResetsAtUtc,
    [property: JsonPropertyName("chat")] AiUsageItemView Chat,
    [property: JsonPropertyName("scan")] AiUsageItemView Scan);

public sealed record AiUsageReservation(Guid Id, bool Enforced);

public interface IAiUsageService
{
    Task<AiUsageView> GetAsync(Guid userId, CancellationToken ct);
    Task<AiUsageReservation> ReserveAsync(Guid userId, string kind, CancellationToken ct);
    Task CommitAsync(AiUsageReservation reservation, CancellationToken ct);
    Task ReleaseAsync(AiUsageReservation reservation, CancellationToken ct);
}
