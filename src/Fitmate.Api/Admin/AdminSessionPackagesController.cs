using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/session-packages")]
public sealed class AdminSessionPackagesController(
    SessionPackageReadService packages, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public Task<SessionPackagePageView> List(
        [FromQuery] string? status, [FromQuery] Guid? memberId,
        [FromQuery] Guid? ptUserId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        packages.ListForAdminAsync(current.GetUserIdOrThrow(), ParseStatus(status),
            memberId, ptUserId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<SessionPackageView> Get(Guid id, CancellationToken ct) =>
        packages.GetForAdminAsync(current.GetUserIdOrThrow(), id, ct);

    private static SessionPackageStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Enum.GetNames<SessionPackageStatus>().Any(name =>
                string.Equals(name, raw, StringComparison.OrdinalIgnoreCase)) &&
            Enum.TryParse<SessionPackageStatus>(raw, true, out var parsed) &&
            Enum.IsDefined(parsed)) return parsed;
        throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
            "Trạng thái gói tập không hợp lệ.",
            new Dictionary<string, string[]> { ["status"] = ["unknown package status"] });
    }
}
