using System.Text.Json.Serialization;
using Fitmate.Domain.Support;

namespace Fitmate.Application.Support;

public record CreateSupportTicketRequest(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("attachment_urls")] List<string>? AttachmentUrls,
    [property: JsonPropertyName("booking_id")] Guid? BookingId = null);

public record ReplySupportTicketRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("attachment_urls")] List<string>? AttachmentUrls);

public record AdminChangeStatusRequest(
    [property: JsonPropertyName("status")] string Status);

public record SupportTicketMessageDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sender_id")] Guid SenderId,
    [property: JsonPropertyName("is_admin")] bool IsAdmin,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("attachment_urls")] List<string> AttachmentUrls,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public record SupportTicketSummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
    [property: JsonPropertyName("resolved_at")] DateTime? ResolvedAt,
    [property: JsonPropertyName("closed_at")] DateTime? ClosedAt,
    [property: JsonPropertyName("booking_id")] Guid? BookingId = null);

public record SupportTicketDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
    [property: JsonPropertyName("resolved_at")] DateTime? ResolvedAt,
    [property: JsonPropertyName("closed_at")] DateTime? ClosedAt,
    [property: JsonPropertyName("messages")] IReadOnlyList<SupportTicketMessageDto> Messages,
    [property: JsonPropertyName("booking_id")] Guid? BookingId = null,
    [property: JsonPropertyName("booking_resolution")] string? BookingResolution = null,
    [property: JsonPropertyName("booking_resolved_at_utc")] DateTime? BookingResolvedAtUtc = null,
    [property: JsonPropertyName("booking_resolution_reason")] string? BookingResolutionReason = null);

public record PagedSupportTicketsResult(
    [property: JsonPropertyName("items")] IReadOnlyList<SupportTicketSummaryDto> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);
