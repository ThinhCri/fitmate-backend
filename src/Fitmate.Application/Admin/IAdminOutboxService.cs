namespace Fitmate.Application.Admin;

public record DeadLetterSummaryDto(
    Guid Id,
    string Type,
    int Attempts,
    DateTime CreatedAt,
    string Status,
    string? LastError);

public record DeadLetterPagedResult(
    IReadOnlyList<DeadLetterSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record OutboxStatsDto(
    int PendingCount,
    int DeadLetterCount,
    double OldestPendingAgeSeconds);

public record ReplayOutboxResult(
    Guid Id,
    string Type,
    string Status,
    Guid ReplayedBy,
    DateTime NextAttemptAt);

public interface IAdminOutboxService
{
    Task<OutboxStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<DeadLetterPagedResult> ListDeadLettersAsync(int page, int pageSize, string? typeFilter, CancellationToken ct = default);
    Task<ReplayOutboxResult> ReplayAsync(Guid messageId, Guid adminUserId, string reason, CancellationToken ct = default);
}

