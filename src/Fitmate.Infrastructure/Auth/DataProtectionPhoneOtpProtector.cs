using Fitmate.Application.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Fitmate.Infrastructure.Auth;

public sealed class DataProtectionPhoneOtpProtector(IDataProtectionProvider provider) : IPhoneOtpProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("fitmate.phone-otp.v1");
    public string Protect(string value) => _protector.Protect(value);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
