using System.Security.Claims;
using Fitmate.Api.Realtime;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Fitmate.Api.Ai;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.UnitTests;
public sealed class AdminFinanceRealtimeTests
{
    [Fact]
    public void Priority_sync_queue_is_bounded_and_translates_to_PostgreSQL()
    {
        using var db = new FitmateDbContext(new DbContextOptionsBuilder<FitmateDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused").Options);
        var sql = AiPlayOrderSyncWorker.DueIds(db, DateTime.UtcNow).ToQueryString();
        sql.Should().Contain("LIMIT").And.Contain("ORDER BY").And.Contain("DeveloperRevenueVnd");
    }
    [Fact]
    public async Task Event_only_invalidates_admin_group_without_order_or_customer_payload()
    {
        var client = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(AdminFinanceHub.Group)).Returns(client.Object);
        var hub = new Mock<IHubContext<AdminFinanceHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        await new AdminFinanceChangedHandler(hub.Object).HandleAsync("{}", default);
        client.Verify(x => x.SendCoreAsync("finance.updated", It.Is<object?[]>(a => a.Length == 1 && System.Text.Json.JsonSerializer.Serialize(a[0]) == "{\"scope\":\"ai-sales\"}"), default), Times.Once);
        clients.Verify(x => x.All, Times.Never);
    }
    [Fact]
    public async Task Joining_live_finance_requires_application_admin_authorization()
    {
        var id = Guid.NewGuid(); var auth = new Mock<IAuthorizationGuard>();
        auth.Setup(x => x.EnsureRoleAsync(id, "Admin", It.IsAny<CancellationToken>())).ThrowsAsync(new DomainException("FORBIDDEN", "Forbidden", 403));
        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test")));
        var groups = new Mock<IGroupManager>();
        var hub = new AdminFinanceHub(auth.Object) { Context = context.Object, Groups = groups.Object };
        await FluentActions.Invoking(() => hub.OnConnectedAsync()).Should().ThrowAsync<DomainException>();
        groups.VerifyNoOtherCalls();
    }
}
