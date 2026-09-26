using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Fitmate.Api.Admin;

// Application mutations invoke this verifier before any financial state change.
// No controller-only bypass and no assumption that an ordinary JWT proves MFA.
public sealed class FinanceStepUp(IHttpContextAccessor http, ICurrentUserAccessor current,
    UserManager<ApplicationUser> users) : IFinanceStepUp
{
    public async Task EnsureAsync(Guid adminId, CancellationToken ct)
    {
        if (current.GetUserIdOrThrow() != adminId)
            throw new ForbiddenException("FINANCE_MFA_REQUIRED", "Administrator step-up authentication is required.");
        var user = await users.FindByIdAsync(adminId.ToString());
        var code = http.HttpContext?.Request.Headers["X-Finance-Totp"].ToString();
        if (user == null || user.IsDeleted || user.Status != UserStatus.Active ||
            await users.IsLockedOutAsync(user) || !await users.GetTwoFactorEnabledAsync(user) ||
            code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
            throw new ForbiddenException("FINANCE_MFA_REQUIRED", "Provide a current six-digit authenticator code in X-Finance-Totp.");
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            await users.AccessFailedAsync(user);
            throw new ForbiddenException("FINANCE_MFA_INVALID", "Invalid or expired authenticator code.");
        }
        await users.ResetAccessFailedCountAsync(user);
    }
}
