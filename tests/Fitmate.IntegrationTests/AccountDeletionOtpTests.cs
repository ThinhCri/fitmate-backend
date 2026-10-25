using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fitmate.Application.Identity;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Auth;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class AccountDeletionOtpTests(FitmateTestAppFactory factory) : IClassFixture<FitmateTestAppFactory>
{
    [Theory]
    [InlineData("/api/v1/users/me/deletion-otp")]
    [InlineData("/api/v1/users/me/delete-request")]
    public async Task AnonymousRequests_AreUnauthorized(string path)
    {
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync(path, new { otp = "123456" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("attempts")]
    [InlineData("expired")]
    [InlineData("email_changed")]
    [InlineData("other_user")]
    public async Task Challenge_EnforcesDurableVerification(string scenario)
    {
        factory.EnsureDatabaseInitialized();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDeletionTokenProtector>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid():N}@example.test", SecurityStamp = Guid.NewGuid().ToString(), FullName = "OTP integration", Status = UserStatus.Active };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var outbox = new CaptureOutbox();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var gateway = new IdentityAccountDeletionOtpGateway(db, outbox, protector, users);
        var start = await gateway.StartAsync(user.Id, default);
        start.ResendAfterSeconds.Should().Be(60);
        user.DeletionRequestedAt.Should().BeNull();
        var payload = outbox.Payload!;
        var code = protector.Unprotect(payload.ProtectedCode);
        var challengeJson = JsonNode.Parse((await db.UserTokens.SingleAsync(t => t.UserId == user.Id)).Value!)!;
        var purpose = challengeJson["Purpose"]!.GetValue<string>();
        purpose.Should().StartWith("account-deletion:").And.NotBe("email-verification");
        (await users.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider,
            purpose, code)).Should().BeTrue();
        payload.ProtectedCode.Should().NotBe(code);
        var resend = () => gateway.StartAsync(user.Id, default);
        (await resend.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("DELETION_OTP_RESEND_TOO_SOON");

        var calls = 0;
        Task<AccountDeletionStatusDto> Mutation()
        {
            calls++;
            user.DeletionRequestedAt = DateTime.UtcNow;
            return Task.FromResult(new AccountDeletionStatusDto(user.Id, "active", true, user.DeletionRequestedAt, null, 0, false));
        }
        if (scenario == "attempts")
        {
            var wrong = code == "000000" ? "000001" : "000000";
            for (var i = 0; i < 5; i++)
            {
                var fail = () => gateway.ExecuteVerifiedAsync(user.Id, wrong, Mutation, default);
                await fail.Should().ThrowAsync<DomainException>();
            }
        }
        if (scenario == "expired")
        {
            var row = await db.UserTokens.SingleAsync(t => t.UserId == user.Id);
            var json = JsonNode.Parse(row.Value!)!;
            json["ExpiresAt"] = DateTime.UtcNow.AddSeconds(-1);
            row.Value = json.ToJsonString();
            await db.SaveChangesAsync();
        }
        if (scenario == "email_changed") { user.Email = "changed@example.test"; await db.SaveChangesAsync(); }
        var target = user.Id;
        if (scenario == "other_user")
        {
            var other = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString(), Email = "other@example.test", FullName = "Other", Status = UserStatus.Active };
            db.Users.Add(other); await db.SaveChangesAsync(); target = other.Id;
        }
        var verify = () => gateway.ExecuteVerifiedAsync(target, code, Mutation, default);
        if (scenario == "success")
        {
            (await verify()).IsPendingDeletion.Should().BeTrue();
            calls.Should().Be(1);
            await verify.Should().ThrowAsync<DomainException>();
            calls.Should().Be(1);
            (await db.UserTokens.AnyAsync(t => t.UserId == user.Id)).Should().BeFalse();
        }
        else
        {
            (await verify.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("DELETION_OTP_INVALID");
            calls.Should().Be(0);
        }
    }

    private sealed class CaptureOutbox : IOutboxDispatcher
    {
        public DeletionOtpEmailPayload? Payload { get; private set; }
        public void Enqueue(string type, object payload) => Payload = (DeletionOtpEmailPayload)payload;
    }
}
