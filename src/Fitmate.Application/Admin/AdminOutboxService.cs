using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

/// <summary>
/// BE-824: Service nghiệp vụ quản lý Outbox Dead-Letter Queue và Gated Replay cho Admin.
/// Tuân thủ quy tắc kiểm soát quyền, bảo vệ chống replay đồng thời và bảo toàn kiểm toán (audit).
/// </summary>
public class AdminOutboxService : IAdminOutboxService
{
    private readonly IOutboxStore _outboxStore;

    public AdminOutboxService(IOutboxStore outboxStore)
    {
        _outboxStore = outboxStore;
    }

    public async Task<OutboxStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var pendingCount = await _outboxStore.GetPendingCountAsync(ct);
        var deadLetterCount = await _outboxStore.GetDeadLetterCountAsync(ct);
        var oldestAge = await _outboxStore.GetOldestPendingAgeAsync(DateTime.UtcNow, ct);

        return new OutboxStatsDto(
            PendingCount: pendingCount,
            DeadLetterCount: deadLetterCount,
            OldestPendingAgeSeconds: oldestAge?.TotalSeconds ?? 0);
    }

    public async Task<DeadLetterPagedResult> ListDeadLettersAsync(int page, int pageSize, string? typeFilter, CancellationToken ct = default)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await _outboxStore.FetchDeadLettersPagedAsync(safePage, safePageSize, typeFilter, ct);

        var dtos = items.Select(m => new DeadLetterSummaryDto(
            Id: m.Id,
            Type: m.Type,
            Attempts: m.Attempts,
            CreatedAt: m.CreatedAt,
            Status: m.Status,
            LastError: SanitizeLastError(m.LastError)
        )).ToList();

        return new DeadLetterPagedResult(dtos, total, safePage, safePageSize);
    }

    public async Task<ReplayOutboxResult> ReplayAsync(Guid messageId, Guid adminUserId, string reason, CancellationToken ct = default)
    {
        if (adminUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu quyền quản trị viên hợp lệ để thực hiện replay.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            var errors = new Dictionary<string, string[]>
            {
                ["reason"] = new[] { "Lý do replay phải có ít nhất 5 ký tự." }
            };
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Lý do replay không hợp lệ.", errors);
        }

        // 1. Kiểm tra sự tồn tại của tin nhắn
        var msg = await _outboxStore.FindByIdAsync(messageId, ct);
        if (msg == null)
        {
            throw new NotFoundException(ErrorCodes.Outbox.NotFound, $"Không tìm thấy outbox message với ID: {messageId}");
        }

        // 2. Kiểm tra trạng thái hiện tại
        if (msg.Status != "dead_letter")
        {
            throw new ConflictException(ErrorCodes.Outbox.InvalidStatus,
                $"Chỉ có thể replay tin nhắn ở trạng thái 'dead_letter'. Trạng thái hiện tại: '{msg.Status}'.");
        }

        // 3. Financial Replay Guard: Nếu là sự kiện thanh toán hoặc hoàn tiền đã từng bị lỗi
        // Cần bảo vệ chống duplicate money mutation nếu chưa kiểm tra đối soát
        if (IsFinancialEvent(msg.Type))
        {
            // Kiểm tra xem message có dấu hiệu duplicate rủi ro không
            if (msg.Attempts >= 10)
            {
                throw new ConflictException(ErrorCodes.Outbox.FinancialReplayBlocked,
                    $"Tin nhắn tài chính {msg.Type} đã thử {msg.Attempts} lần thất bại. Bắt buộc đối soát sổ cái kế toán trước khi cho phép kích hoạt replay.");
            }
        }

        // 4. Atomic check & update chống tranh chấp đồng thời
        var success = await _outboxStore.TryReplayDeadLetterAsync(messageId, adminUserId, reason.Trim(), ct);
        if (!success)
        {
            throw new ConflictException(ErrorCodes.Outbox.ConcurrentMutation,
                "Tin nhắn đang được xử lý hoặc đã được replay bởi một quản trị viên khác.");
        }

        return new ReplayOutboxResult(
            Id: messageId,
            Type: msg.Type,
            Status: "pending",
            ReplayedBy: adminUserId,
            NextAttemptAt: DateTime.UtcNow);
    }

    private static bool IsFinancialEvent(string type)
    {
        return type.StartsWith("payment.", StringComparison.OrdinalIgnoreCase) ||
               type.StartsWith("finance.", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("refund", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeLastError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return error;
        // Loại bỏ kết nối DB, mật khẩu, JWT token khỏi thông báo lỗi trả về cho client
        var sanitized = error;
        if (sanitized.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(?i)Password=[^;]+", "Password=[REDACTED]");
        }
        if (sanitized.Contains("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(?i)Bearer\s+[A-Za-z0-9_\-\.]+", "Bearer [REDACTED]");
        }
        return sanitized.Length > 500 ? sanitized[..500] + "..." : sanitized;
    }
}
