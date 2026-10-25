using System.Security.Claims;
using Fitmate.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Identity;

[ApiController]
public class AccountDeletionController : ControllerBase
{
    private readonly IAccountDeletionService _deletionService;

    public AccountDeletionController(IAccountDeletionService deletionService)
    {
        _deletionService = deletionService;
    }

    [HttpPost("api/v1/users/me/delete-request")]
    [HttpDelete("api/v1/users/me")]
    [Authorize]
    public async Task<IActionResult> RequestDeletion([FromBody] AccountDeletionRequest? request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var result = await _deletionService.RequestDeletionAsync(userId.Value, request ?? new AccountDeletionRequest(null, null), ct);
        return Ok(result);
    }

    [HttpPost("api/v1/users/me/deletion-otp")]
    [Authorize]
    public async Task<IActionResult> SendDeletionOtp(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _deletionService.SendDeletionOtpAsync(userId.Value, ct));
    }

    [HttpGet("api/v1/users/me/deletion-status")]
    [Authorize]
    public async Task<IActionResult> GetDeletionStatus(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var result = await _deletionService.GetDeletionStatusAsync(userId.Value, ct);
        return Ok(result);
    }

    [HttpPost("api/v1/users/me/cancel-deletion")]
    [Authorize]
    public async Task<IActionResult> CancelDeletion(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var result = await _deletionService.CancelDeletionAsync(userId.Value, ct);
        return Ok(result);
    }

    [HttpPost("api/v1/users/me/export")]
    [Authorize]
    public async Task<IActionResult> ExportUserData(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var result = await _deletionService.ExportUserDataAsync(userId.Value, ct);
        return Ok(result);
    }

    [HttpPost("api/v1/public/account-deletion-request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestPublicDeletion([FromBody] PublicAccountDeletionRequest request, CancellationToken ct)
    {
        await _deletionService.RequestPublicDeletionAsync(request, ct);
        return Accepted(new { message = "If the account exists, a deletion verification email has been dispatched." });
    }

    [HttpPost("api/v1/public/account-deletion-confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmPublicDeletion([FromBody] PublicAccountDeletionConfirm request, CancellationToken ct)
    {
        var result = await _deletionService.ConfirmPublicDeletionAsync(request.Token, ct);
        return Ok(result);
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

