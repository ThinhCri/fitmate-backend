namespace Fitmate.Application.Auth;

public interface IPhoneOtpProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
