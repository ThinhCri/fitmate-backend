using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Common;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-828: Bộ kiểm chứng tương thích runtime (Runtime Compatibility Tests) trên môi trường hiện tại.
/// Kiểm chứng các bất biến về DataProtection (mã hóa/giải mã và từ chối khi sai key/purpose),
/// xác thực mật khẩu cũ (BCrypt / PBKDF2), tính ổn định của JSON serialization và JWT token validation.
/// </summary>
public class RuntimeCompatibilityTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void DataProtection_Decryption_Succeeds_For_Valid_Tokens_And_Fails_For_Invalid_Purpose_Or_Keys()
    {
        var tempKeysDirA = Path.Combine(Path.GetTempPath(), "fitmate_compat_keys_a_" + Guid.NewGuid());
        var tempKeysDirB = Path.Combine(Path.GetTempPath(), "fitmate_compat_keys_b_" + Guid.NewGuid());
        Directory.CreateDirectory(tempKeysDirA);
        Directory.CreateDirectory(tempKeysDirB);

        try
        {
            // 1. Khởi tạo provider A với key ring A
            var servicesA = new ServiceCollection();
            servicesA.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(tempKeysDirA))
                .SetApplicationName("Fitmate.Api");
            using var spA = servicesA.BuildServiceProvider();
            var providerA = spA.GetRequiredService<IDataProtectionProvider>();

            var correctPurpose = "fitmate.compat-purpose.v1";
            var wrongPurpose = "fitmate.different-purpose.v1";
            var expectedPlaintext = "sensitive-user-token-payload-2026";

            var protectorA = providerA.CreateProtector(correctPurpose);
            var ciphertext = protectorA.Protect(expectedPlaintext);
            ciphertext.Should().NotBeNullOrWhiteSpace();

            // 2. Trường hợp thành công: Giải mã bằng cùng provider và cùng purpose
            var decrypted = protectorA.Unprotect(ciphertext);
            decrypted.Should().Be(expectedPlaintext);

            // 3. Trường hợp từ chối an toàn: Sai Purpose -> bắt buộc ném CryptographicException fail-closed
            var wrongProtector = providerA.CreateProtector(wrongPurpose);
            var actWrongPurpose = () => wrongProtector.Unprotect(ciphertext);
            actWrongPurpose.Should().Throw<CryptographicException>();

            // 4. Trường hợp từ chối an toàn: Sai Key Ring (provider B) -> bắt buộc ném CryptographicException
            var servicesB = new ServiceCollection();
            servicesB.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(tempKeysDirB))
                .SetApplicationName("Fitmate.Api");
            using var spB = servicesB.BuildServiceProvider();
            var providerB = spB.GetRequiredService<IDataProtectionProvider>();
            var protectorB = providerB.CreateProtector(correctPurpose);

            var actWrongKey = () => protectorB.Unprotect(ciphertext);
            actWrongKey.Should().Throw<CryptographicException>();
        }
        finally
        {
            if (Directory.Exists(tempKeysDirA)) Directory.Delete(tempKeysDirA, true);
            if (Directory.Exists(tempKeysDirB)) Directory.Delete(tempKeysDirB, true);
        }
    }

    [Fact]
    public void BcryptPasswordHasher_Verifies_Correct_Password_And_Rejects_Wrong_Or_Corrupted_Password()
    {
        var hasher = new BcryptPasswordHasher();
        var rawPassword = "StrongP@ssw0rd2026!#$";
        var wrongPassword = "WrongPassword123";

        // 1. Tạo hash
        var hash = hasher.Hash(rawPassword);
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().StartWith("$2"); // BCrypt prefix

        // 2. Xác thực mật khẩu đúng
        var isMatch = hasher.Verify(rawPassword, hash);
        isMatch.Should().BeTrue();

        // 3. Từ chối mật khẩu sai
        var isWrongMatch = hasher.Verify(wrongPassword, hash);
        isWrongMatch.Should().BeFalse();

        // 4. Từ chối đầu vào rỗng hoặc null
        hasher.Verify(string.Empty, hash).Should().BeFalse();
        hasher.Verify(rawPassword, string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Identity_PasswordHasher_Verifies_Correct_Password_And_Detects_Invalid_Password()
    {
        var identityHasher = new PasswordHasher<ApplicationUser>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser@fitmate.local",
            Email = "testuser@fitmate.local"
        };

        var rawPassword = "SecureUserPass!2026";
        var wrongPassword = "IncorrectPass!2026";

        // 1. Tạo hash qua ASP.NET Core Identity PBKDF2
        var hashedPassword = identityHasher.HashPassword(user, rawPassword);
        hashedPassword.Should().NotBeNullOrWhiteSpace();

        // 2. Xác thực mật khẩu đúng
        var verifySuccess = identityHasher.VerifyHashedPassword(user, hashedPassword, rawPassword);
        verifySuccess.Should().Be(PasswordVerificationResult.Success);

        // 3. Từ chối mật khẩu sai
        var verifyFailed = identityHasher.VerifyHashedPassword(user, hashedPassword, wrongPassword);
        verifyFailed.Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void JwtTokenService_Issues_Valid_Token_And_Validates_Claims_Correctly()
    {
        var jwtOptions = new JwtOptions
        {
            SigningKey = "SuperSecretFitmateJwtSigningKey2026MustBeAtLeast32BytesLong!",
            Issuer = "fitmate-test",
            Audience = "fitmate-clients",
            AccessTokenTtlSeconds = 300
        };

        var tokenService = new JwtTokenService(jwtOptions);
        var userId = Guid.NewGuid();
        var email = "member@fitmate.local";
        var role = "Member";
        var fullName = "Nguyen Van A";

        var request = new JwtTokenRequest(userId, email, role, fullName);
        var tokenResult = tokenService.Issue(request);

        tokenResult.Should().NotBeNull();
        tokenResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokenResult.ExpiresInSeconds.Should().Be(300);

        // 1. Xác thực token hợp lệ
        var principal = tokenService.Validate(tokenResult.AccessToken);
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be(userId.ToString());
        principal.FindFirst(ClaimTypes.Email)?.Value.Should().Be(email);
        principal.FindFirst(ClaimTypes.Role)?.Value.Should().Be(role);
        principal.FindFirst("full_name")?.Value.Should().Be(fullName);
    }

    [Fact]
    public void JwtTokenService_Rejects_Expired_Tokens()
    {
        var jwtOptions = new JwtOptions
        {
            SigningKey = "SuperSecretFitmateJwtSigningKey2026MustBeAtLeast32BytesLong!",
            Issuer = "fitmate-test",
            Audience = "fitmate-clients",
            AccessTokenTtlSeconds = 300
        };

        // Tạo token có thời gian hiệu lực hoàn toàn trong quá khứ (notBefore: -30m, expires: -10m)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiredJwt = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10),
            signingCredentials: creds);

        var expiredTokenString = new JwtSecurityTokenHandler().WriteToken(expiredJwt);

        var tokenService = new JwtTokenService(jwtOptions);

        // Validate token đã hết hạn -> bắt buộc trả về null (401)
        var principal = tokenService.Validate(expiredTokenString);
        principal.Should().BeNull("Token đã quá hạn sử dụng không được phép validate thành công");
    }

    [Fact]
    public void JsonSerialization_Preserves_Contract_Conventions_From_Existing_Fixtures()
    {
        var payload = new
        {
            bookingId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            amountVnd = 500000L,
            status = "Confirmed",
            createdAt = new DateTimeOffset(2026, 9, 23, 8, 30, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(payload, CamelCaseOptions);
        json.Should().Contain("\"bookingId\":\"11111111-1111-1111-1111-111111111111\"");
        json.Should().Contain("\"amountVnd\":500000");
        json.Should().Contain("\"status\":\"Confirmed\"");
        json.Should().Contain("\"createdAt\":\"2026-09-23T08:30:00+00:00\"");

        // Deserialization roundtrip
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("bookingId").GetString().Should().Be("11111111-1111-1111-1111-111111111111");
        root.GetProperty("amountVnd").GetInt64().Should().Be(500000L);
    }
}
