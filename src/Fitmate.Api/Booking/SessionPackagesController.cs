using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Booking;

[ApiController]
[Route("api/v1/me/session-packages")]
[Authorize]
public sealed class SessionPackagesController(
    SessionPackageReadService packages, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public Task<SessionPackagePageView> ListMine([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        packages.ListMineAsync(current.GetUserIdOrThrow(), page, pageSize, ct);

    [HttpGet("assigned")]
    [Authorize(Roles = "PT")]
    public Task<SessionPackagePageView> ListAssigned([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        packages.ListAssignedPtAsync(current.GetUserIdOrThrow(), page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<SessionPackageView> Get(Guid id, CancellationToken ct) =>
        packages.GetForParticipantAsync(current.GetUserIdOrThrow(), id, ct);
}
