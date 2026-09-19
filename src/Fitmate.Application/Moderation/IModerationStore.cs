using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

public interface IModerationStore
{
    Task<(IReadOnlyList<ReportRow> Items, long Total)> ListReportsAsync(
        ReportStatus? status, int skip, int take, CancellationToken ct);

    Task<ReportRow?> GetReportAsync(Guid reportId, CancellationToken ct);

    /// <summary>BE-211: tìm Open report trùng từ cùng reporter cho cùng target — chống spam (DEC-028).</summary>
    Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct);

    Task SaveReportAsync(ReportRow row, CancellationToken ct);

    /// <summary>BE-211: soft-delete Post khi action=DeleteContent.</summary>
    Task SoftDeletePostAsync(Guid postId, CancellationToken ct);

    /// <summary>BE-211: soft-delete Comment khi action=DeleteContent.</summary>
    Task SoftDeleteCommentAsync(Guid commentId, CancellationToken ct);

    /// <summary>BE-211: commit transaction cho soft-delete + report update cùng DbContext.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
