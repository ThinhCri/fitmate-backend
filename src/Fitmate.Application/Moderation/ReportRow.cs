using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

/// <summary>Internal data row truyền giữa store ↔ service cho Report entity.
/// Định nghĩa trong <c>IModerationStore</c> và được re-export qua <c>ISocialStore</c>
/// vì Social cần query bảng <c>reports</c> cho idempotency / spam check.</summary>
public sealed record ReportRow(
    Guid Id,
    Guid ReporterId,
    ReportTargetType TargetType,
    Guid TargetId,
    string Reason,
    string? Details,
    ReportStatus Status,
    string? AdminNote,
    Guid? ResolvedByAdminId,
    string? IdempotencyKey,
    string? ResolveAction,
    DateTime CreatedAt,
    DateTime UpdatedAt);
