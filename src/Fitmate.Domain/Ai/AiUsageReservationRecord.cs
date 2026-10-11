namespace Fitmate.Domain.Ai;

public sealed class AiUsageReservationRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateOnly LocalDate { get; set; }
    public string State { get; set; } = "reserved";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
