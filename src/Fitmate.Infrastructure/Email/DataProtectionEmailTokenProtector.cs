using Fitmate.Application.Email;
using Microsoft.AspNetCore.DataProtection;

namespace Fitmate.Infrastructure.Email;

public class DataProtectionEmailTokenProtector : IEmailTokenProtector
{
    private readonly IDataProtector _protector;
    private const string Purpose = "fitmate.email-tokens.v1";

    public DataProtectionEmailTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plainToken)
    {
        ArgumentNullException.ThrowIfNull(plainToken);
        return _protector.Protect(plainToken);
    }

    public string Unprotect(string protectedToken)
    {
        ArgumentNullException.ThrowIfNull(protectedToken);
        return _protector.Unprotect(protectedToken);
    }
}
