using Fitmate.Api.Auth;
using Fitmate.Application.Preferences;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Identity;

/// <summary>
/// BE-819 / BR-15: User preferences endpoint.
/// Allows users to view and update their application preferences and consents.
/// </summary>
[ApiController]
[Route("api/v1/users/me/preferences")]
[Authorize]
public class UserPreferencesController : ControllerBase
{
    private readonly IUserPreferenceService _preferences;
    private readonly ICurrentUserAccessor _currentUser;

    public UserPreferencesController(
        IUserPreferenceService preferences,
        ICurrentUserAccessor currentUser)
    {
        _preferences = preferences;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        var pref = await _preferences.GetByUserIdAsync(userId, ct);
        return Ok(pref);
    }

    [HttpPut]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdateUserPreferenceRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        var updated = await _preferences.UpdateAsync(userId, request, ct);
        return Ok(updated);
    }
}

