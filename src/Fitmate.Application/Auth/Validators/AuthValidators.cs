using Fitmate.Application.Auth.Contracts;
using Fitmate.Domain.Common;
using FluentValidation;

namespace Fitmate.Application.Auth.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address")
            .MaximumLength(254);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(PasswordPolicy.MinimumLength).WithMessage(PasswordPolicy.MinimumLengthMessage)
            .MaximumLength(PasswordPolicy.MaximumLength);

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(200);

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(r => r == "member" || r == "pt")
            .WithMessage("Role must be 'member' or 'pt'");

        // Member-only fields: chỉ validate khi role=member
        When(x => x.Role == "member", () =>
        {
            RuleFor(x => x.HeightCm!.Value)
                .GreaterThan(0).LessThanOrEqualTo(300)
                .When(x => x.HeightCm.HasValue);
            RuleFor(x => x.WeightKg!.Value)
                .GreaterThan(0).LessThanOrEqualTo(500)
                .When(x => x.WeightKg.HasValue);
            RuleFor(x => x.Gender)
                .Must(g => g == "male" || g == "female" || g == "other")
                .When(x => !string.IsNullOrEmpty(x.Gender));
        });

        // PT-only fields: chỉ validate khi role=pt
        When(x => x.Role == "pt", () =>
        {
            RuleFor(x => x.Specialty)
                .NotEmpty().WithMessage("Specialty is required for PT")
                .MaximumLength(200);
            RuleFor(x => x.ExperienceYears)
                .NotNull().WithMessage("experience_years is required for PT")
                .GreaterThanOrEqualTo(0).LessThanOrEqualTo(70)
                .When(x => x.ExperienceYears.HasValue);
            RuleFor(x => x.PricePerSessionVnd)
                .NotNull().WithMessage("price_per_session_vnd is required for PT")
                .GreaterThanOrEqualTo(0)
                .When(x => x.PricePerSessionVnd.HasValue);
        });

        RuleFor(x => x.Timezone)
            .MaximumLength(64);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password)
            .NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeviceLabel)
            .MaximumLength(200);
    }
}

public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("refresh_token is required")
            .MaximumLength(256);
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address")
            .MaximumLength(254);
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .Matches("^[0-9]{6}$").WithMessage("Reset code must contain exactly 6 digits");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(PasswordPolicy.MinimumLength).WithMessage(PasswordPolicy.MinimumLengthMessage)
            .MaximumLength(PasswordPolicy.MaximumLength);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address")
            .MaximumLength(254);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(PasswordPolicy.MinimumLength).WithMessage(PasswordPolicy.MinimumLengthMessage)
            .MaximumLength(PasswordPolicy.MaximumLength)
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from current password");
    }
}

public class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Code).Matches("^[0-9]{6}$").WithMessage("Verification code must contain exactly 6 digits");
    }
}

public class ResendEmailVerificationRequestValidator : AbstractValidator<ResendEmailVerificationRequest>
{
    public ResendEmailVerificationRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
    }
}
