using Fitmate.Domain.Support;

namespace Fitmate.Application.Support;

public record CreateSupportTicketRequest(
    string Subject,
    string Category,
    string? Priority,
    string Message,
    List<string>? AttachmentUrls);

public record ReplySupportTicketRequest(
    string Message,
    List<string>? AttachmentUrls);

public record AdminChangeStatusRequest(
    string Status);

public record SupportTicketMessageDto(
    Guid Id,
    Guid SenderId,
    bool IsAdmin,
    string Content,
    List<string> AttachmentUrls,
    DateTime CreatedAt);

public record SupportTicketSummaryDto(
    Guid Id,
    Guid UserId,
    string Subject,
    string Category,
    string Priority,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    DateTime? ClosedAt);

public record SupportTicketDetailDto(
    Guid Id,
    Guid UserId,
    string Subject,
    string Category,
    string Priority,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    IReadOnlyList<SupportTicketMessageDto> Messages);

public record PagedSupportTicketsResult(
    IReadOnlyList<SupportTicketSummaryDto> Items,
    int Total,
    int Page,
    int PageSize);

