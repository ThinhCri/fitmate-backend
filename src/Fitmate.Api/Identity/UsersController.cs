using System.Security.Claims;
using Fitmate.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Identity;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IUserProfileService _profileService;

    public UsersController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var profile = await _profileService.GetMyProfileAsync(userId.Value, ct);
        return Ok(profile);
    }

    [HttpPatch("me")]
    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UserProfileUpdateRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var updated = await _profileService.UpdateMyProfileAsync(userId.Value, request, ct);
        return Ok(updated);
    }

    [HttpPut("me/avatar")]
    [Authorize]
    public async Task<IActionResult> AttachAvatar([FromBody] AttachAvatarRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _profileService.AttachAvatarAsync(userId.Value, request, ct));
    }

    [HttpGet("{id:guid}/profile")]
    [Authorize]
    public async Task<IActionResult> GetPublicProfile([FromRoute] Guid id, CancellationToken ct)
    {
        var publicProfile = await _profileService.GetPublicProfileAsync(id, ct);
        return Ok(publicProfile);
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<IActionResult> SearchUsers([FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<PublicUserProfileDto>());

        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();
        var results = await _profileService.SearchUsersAsync(userId.Value, q, limit, ct);
        return Ok(results);
    }

    [HttpGet("suggestions")]
    [Authorize]
    public async Task<IActionResult> ListSuggestedUsers([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _profileService.ListSuggestedUsersAsync(userId.Value, limit, ct));
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

