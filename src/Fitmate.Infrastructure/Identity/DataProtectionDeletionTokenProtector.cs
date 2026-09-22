using Fitmate.Application.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Fitmate.Infrastructure.Identity;

public class DataProtectionDeletionTokenProtector : IDeletionTokenProtector
{
    private readonly IDataProtector _protector;
    private const string Purpose = "fitmate.public-account-deletion.v1";

    public DataProtectionDeletionTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        return _protector.Unprotect(protectedText);
    }
}

