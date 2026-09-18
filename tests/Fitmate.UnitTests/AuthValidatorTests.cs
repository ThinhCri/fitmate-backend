using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Auth.Validators;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-030: Auth DTO validators (FluentValidation).
/// </summary>
public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _reg = new();
    private readonly LoginRequestValidator _login = new();
    private readonly RefreshRequestValidator _refresh = new();

    [Fact]
    public async Task RegisterRequest_Valid_Member()
    {
        var req = new RegisterRequest(
            Email: "u@x.vn", Password: "Password123", FullName: "User",
            Role: "member", HeightCm: 170, WeightKg: 65, Gender: "male",
            Birthday: new DateOnly(2000, 1, 1), FitnessGoal: "giảm cân",
            TrainingDays: new List<string> { "mon" },
            Specialty: null, ExperienceYears: null, PricePerSessionVnd: null,
            Timezone: null);
        var r = await _reg.ValidateAsync(req);
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterRequest_InvalidEmail_Fails()
    {
        var req = new RegisterRequest("not-email", "Password123", "User", "member",
            null, null, null, null, null, null, null, null, null, null);
        var r = await _reg.ValidateAsync(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task RegisterRequest_PtRequiresSpecialty()
    {
        var req = new RegisterRequest("pt@x.vn", "Password123", "PT", "pt",
            null, null, null, null, null, null,
            Specialty: null, ExperienceYears: 5, PricePerSessionVnd: 500_000, Timezone: null);
        var r = await _reg.ValidateAsync(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Specialty");
    }

    [Fact]
    public async Task RegisterRequest_InvalidRole_Fails()
    {
        var req = new RegisterRequest("u@x.vn", "Password123", "U", "admin", // admin không qua API
            null, null, null, null, null, null, null, null, null, null);
        var r = await _reg.ValidateAsync(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public async Task LoginRequest_Valid()
    {
        var r = await _login.ValidateAsync(new LoginRequest("u@x.vn", "Password123", null));
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task LoginRequest_MissingEmail_Fails()
    {
        var r = await _login.ValidateAsync(new LoginRequest("", "Password123", null));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshRequest_RequiresToken()
    {
        var r = await _refresh.ValidateAsync(new RefreshRequest(""));
        r.IsValid.Should().BeFalse();
    }
}
