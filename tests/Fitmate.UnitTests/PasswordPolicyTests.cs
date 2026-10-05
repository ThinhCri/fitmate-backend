using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Auth.Validators;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// SET-W01 (chốt 2026-09-28): mật khẩu tối thiểu 12 ký tự ở MỌI nơi.
///
/// Trước khi chốt, các nơi lệch nhau: FluentAuth validators 8, Identity + seed CLI 12,
/// Web Admin form 12, OpenAPI 8. Hệ quả là người dùng đăng ký được mật khẩu 8 ký tự qua
/// API nhưng form Admin lại bắt 12 — một vòng bất khả thi cho chính admin đó.
///
/// Các test ở đây khoá cả hai vế: constant, và việc từng validator dùng constant đó
/// (thay vì copy số 12, để đổi policy sau này chỉ sửa một chỗ).
/// </summary>
public class PasswordPolicyTests
{
    private static RegisterRequest Registration(string password) => new(
        Email: "user@fitmate.vn",
        Password: password,
        FullName: "Nguyen Van A",
        Role: "member",
        HeightCm: null,
        WeightKg: null,
        Gender: null,
        Birthday: null,
        FitnessGoal: null,
        TrainingDays: null,
        Specialty: null,
        ExperienceYears: null,
        PricePerSessionVnd: null,
        Timezone: null);

    [Fact]
    public void MinimumLength_Is_Twelve()
    {
        PasswordPolicy.MinimumLength.Should().Be(12);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Short1!", false)]        // 8 ký tự — từng hợp lệ, giờ không
    [InlineData("ElevenChars", false)]     // 11
    [InlineData("TwelveChars1", true)]     // 12
    [InlineData("TwelveChars1x", true)]
    public void IsAcceptable_Matches_Policy(string? password, bool expected)
    {
        PasswordPolicy.IsAcceptable(password).Should().Be(expected);
    }

    // InlineData không nhận null cho tham số string (CS1041) → case riêng.
    [Fact]
    public void IsAcceptable_Rejects_Null()
    {
        PasswordPolicy.IsAcceptable(null).Should().BeFalse();
    }

    [Fact]
    public void IsAcceptable_Rejects_OverMax()
    {
        PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength + 1)).Should().BeFalse();
        PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength)).Should().BeTrue();
    }

    [Fact]
    public void RegisterValidator_Rejects_EightCharacters()
    {
        var result = new RegisterRequestValidator().Validate(Registration("Short1!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void RegisterValidator_Accepts_TwelveCharacters()
    {
        var result = new RegisterRequestValidator().Validate(Registration("TwelveChars1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ResetPasswordValidator_Rejects_EightCharacters()
    {
        var result = new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest(
            Token: "some-reset-token",
            NewPassword: "Short1!",
            Email: "user@fitmate.vn"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ResetPasswordRequest.NewPassword));
    }

    [Fact]
    public void ChangePasswordValidator_Rejects_EightCharacters()
    {
        var result = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest(
            CurrentPassword: "CurrentPass1",
            NewPassword: "Short1!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordRequest.NewPassword));
    }

    [Fact]
    public void ChangePasswordValidator_StillRejects_SamePassword()
    {
        // Không được đánh đổi quy tắc "khác mật cũ" lấy cho quy tắc độ dài.
        const string reused = "TwelveChars1";
        var result = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest(
            CurrentPassword: reused,
            NewPassword: reused));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordRequest.NewPassword));
    }

    [Fact]
    public void ChangePasswordValidator_Accepts_TwelveCharacters()
    {
        var result = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest(
            CurrentPassword: "CurrentPass1",
            NewPassword: "TwelveChars1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AllPasswordRules_Use_TheSameMessage()
    {
        // Client hiển thị message của BE; nếu 3 chỗ lệch nhau thì admin thấy thông báo khác nhau.
        var register = new RegisterRequestValidator().Validate(Registration("Short1!"));
        var reset = new ResetPasswordRequestValidator()
            .Validate(new ResetPasswordRequest("t", "Short1!", "user@fitmate.vn"));
        var change = new ChangePasswordRequestValidator()
            .Validate(new ChangePasswordRequest("CurrentPass1", "Short1!"));

        foreach (var errors in new[] { register.Errors, reset.Errors, change.Errors })
            errors.Should().Contain(e => e.ErrorMessage == PasswordPolicy.MinimumLengthMessage);
    }
}
