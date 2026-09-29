using Fitmate.Api.Admin;
using Fitmate.Api.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// FIN-07: step-up TOTP là hàng rào giữa một JWT hợp lệ và một thay đổi trạng thái tài chính.
///
/// <see cref="FinanceStepUp"/> bị gọi từ <c>ManualSettlementService</c> (Application layer) trước mọi
/// thay đổi tài chính. Test này khóa từng nhánh từ chối để một lần "tinh giản" điều kiện về sau
/// không vô tình mở đường cho admin chưa bước xác thực hai lớp.
///
/// Phạm vi: chỉ chứng minh logic trong <c>FinanceStepUp</c>. Xác thực TOTP thật cần integration test.
/// </summary>
public class FinanceStepUpTests
{
    private const string ValidCode = "123456";

    private sealed class FixedCodeProvider : IUserTwoFactorTokenProvider<ApplicationUser>
    {
        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
            => Task.FromResult(true);

        public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
            => Task.FromResult(ValidCode);

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
            => Task.FromResult(string.Equals(token, ValidCode, StringComparison.Ordinal));
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId { get; set; }
        public Guid? GetUserId() => UserId;
        public Guid GetUserIdOrThrow() => UserId ?? throw new InvalidOperationException("missing user");
    }

    /// <summary>
    /// UserManager cần IServiceProvider để resolve token provider theo tên đã đăng ký trong
    /// <c>IdentityOptions.Tokens.ProviderMap</c>. Chỉ cần phục vụ đúng provider đó.
    /// </summary>
    private sealed class ServiceProviderStub : IServiceProvider
    {
        private readonly FixedCodeProvider _provider = new();

        public object? GetService(Type serviceType) =>
            typeof(IUserTwoFactorTokenProvider<ApplicationUser>).IsAssignableFrom(serviceType)
                ? _provider
                : null;
    }

    /// <summary>Harness: trả về step-up cùng adminId đã dùng để dựng current user.</summary>
    private static (FinanceStepUp StepUp, Guid AdminId) Build(
        string? totpHeader,
        bool twoFactorEnabled = true,
        bool lockoutEndInFuture = false,
        UserStatus status = UserStatus.Active,
        bool deleted = false,
        bool userMissing = false,
        Guid? callerIdOverride = null)
    {
        var adminId = Guid.NewGuid();
        ApplicationUser? user = new()
        {
            Id = adminId,
            Email = "admin@fitmate.vn",
            UserName = "admin@fitmate.vn",
            TwoFactorEnabled = twoFactorEnabled,
            Status = status,
            DeletedAt = deleted ? DateTime.UtcNow : null,
        };

        // IsLockedOutAsync yêu cầu IUserLockoutStore; UserManager kiểm tra store có hỗ trợ hay không.
        // AccessFailedAsync / ResetAccessFailedCountAsync cũng đi qua IUserLockoutStore.
        var storeWithLockout = new Mock<IUserStore<ApplicationUser>>();
        storeWithLockout.As<IUserLockoutStore<ApplicationUser>>()
            .Setup(s => s.GetLockoutEndDateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser _, CancellationToken _) =>
                lockoutEndInFuture ? DateTimeOffset.UtcNow.AddMinutes(5) : null);
        storeWithLockout.As<IUserLockoutStore<ApplicationUser>>()
            .Setup(s => s.GetAccessFailedCountAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        storeWithLockout.As<IUserLockoutStore<ApplicationUser>>()
            .Setup(s => s.GetLockoutEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeWithLockout.As<IUserLockoutStore<ApplicationUser>>()
            .Setup(s => s.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // .NET 10 đã đổi UpdateAccessFailedCountAsync → IncrementAccessFailedCountAsync.
        storeWithLockout.As<IUserLockoutStore<ApplicationUser>>()
            .Setup(s => s.IncrementAccessFailedCountAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        storeWithLockout.As<IUserEmailStore<ApplicationUser>>()
            .Setup(s => s.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeWithLockout.As<IUserEmailStore<ApplicationUser>>()
            .Setup(s => s.GetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("admin@fitmate.vn");
        // GetTwoFactorEnabledAsync trong UserManager yêu cầu store hỗ trợ IUserTwoFactorStore.
        storeWithLockout.As<IUserTwoFactorStore<ApplicationUser>>()
            .Setup(s => s.GetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser u, CancellationToken _) => u?.TwoFactorEnabled ?? false);
        storeWithLockout.As<IUserTwoFactorStore<ApplicationUser>>()
            .Setup(s => s.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeWithLockout.Setup(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => user is not null && id == adminId.ToString() ? user : null);
        storeWithLockout.Setup(s => s.UpdateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);

        if (userMissing) user = null;

        var options = Options.Create(new IdentityOptions
        {
            Lockout = new LockoutOptions
            {
                AllowedForNewUsers = true,
                MaxFailedAccessAttempts = 5,
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5),
            },
        });
        // TokenOptions.AuthenticatorTokenProvider là TÊN provider (string), không phải object.
        // Đăng ký provider cố định dưới tên đó để VerifyTwoFactorTokenAsync chỉ nhận ValidCode.
        const string ProviderName = "FinanceStepUpTestsFixed";
        options.Value.Tokens.AuthenticatorTokenProvider = ProviderName;
        options.Value.Tokens.ProviderMap[ProviderName] = new TokenProviderDescriptor(typeof(FixedCodeProvider));

        var users = new UserManager<ApplicationUser>(
            storeWithLockout.Object,
            options,
            // .NET 10 không còn NullPasswordHasher; các test ở đây không đi qua đường
            // password nên hasher thật cũng không bao giờ được gọi.
            new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>(),
            new List<IUserValidator<ApplicationUser>>(),
            new List<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceProviderStub(),
            new NullLogger<UserManager<ApplicationUser>>());

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (totpHeader is not null) http.HttpContext!.Request.Headers["X-Finance-Totp"] = totpHeader;

        var current = new FakeCurrentUser { UserId = callerIdOverride ?? adminId };
        return (new FinanceStepUp(http, current, users), adminId);
    }

    [Fact]
    public async Task EnsureAsync_ValidCode_Succeeds()
    {
        var (stepUp, adminId) = Build(ValidCode);

        await stepUp.EnsureAsync(adminId, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureAsync_MissingHeader_Throws()
    {
        var (stepUp, adminId) = Build(totpHeader: null);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Theory]
    [InlineData("12345")]      // ngắn
    [InlineData("1234567")]    // dài
    [InlineData("12a456")]     // có ký tự chữ
    [InlineData("12 456")]     // có khoảng trắng
    [InlineData("１２３４５６")] // full-width digits: phải bị từ chối vì yêu cầu ASCII digit
    public async Task EnsureAsync_NonSixAsciiDigits_Throws(string badCode)
    {
        var (stepUp, adminId) = Build(badCode);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Fact]
    public async Task EnsureAsync_WrongCode_ThrowsInvalid_AndCountsAccessFailure()
    {
        var (stepUp, adminId) = Build("000000");

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_INVALID");
    }

    [Fact]
    public async Task EnsureAsync_TwoFactorDisabled_Throws()
    {
        var (stepUp, adminId) = Build(ValidCode, twoFactorEnabled: false);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Fact]
    public async Task EnsureAsync_IdentityLockoutActive_Throws()
    {
        var (stepUp, adminId) = Build(ValidCode, lockoutEndInFuture: true);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Theory]
    [InlineData(UserStatus.Locked)]
    [InlineData(UserStatus.PendingDeletion)]
    public async Task EnsureAsync_StatusNotActive_Throws(UserStatus status)
    {
        var (stepUp, adminId) = Build(ValidCode, status: status);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Fact]
    public async Task EnsureAsync_SoftDeletedUser_Throws()
    {
        var (stepUp, adminId) = Build(ValidCode, deleted: true);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Fact]
    public async Task EnsureAsync_UserNotFound_Throws()
    {
        var (stepUp, adminId) = Build(ValidCode, userMissing: true);

        var act = async () => await stepUp.EnsureAsync(adminId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }

    [Fact]
    public async Task EnsureAsync_CallerIdentityDiffersFromAdminId_Throws()
    {
        // current user khác adminId được yêu cầu → chặn ngay, không cần tới bước kiểm tra TOTP.
        var (stepUp, _) = Build(ValidCode, callerIdOverride: Guid.NewGuid());

        var act = async () => await stepUp.EnsureAsync(Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("FINANCE_MFA_REQUIRED");
    }
}
