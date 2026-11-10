using Fitmate.Application.Identity;
using Fitmate.Infrastructure.Identity;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class AdminUserPaginationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;
    public AdminUserPaginationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Role_filter_precedes_paging_and_counts_only_matching_primary_roles()
    {
        var marker = "paging" + Guid.NewGuid().ToString("N");
        var trainerIds = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var id = Guid.NewGuid();
            var role = i % 2 == 0 ? "PT" : "Member";
            _factory.CreateAuthenticatedClient(role, id).Dispose();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var user = (await db.Users.FindAsync(id))!;
            user.Email = $"{marker}{i:D2}@example.test";
            user.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (role == "PT") trainerIds.Add(id);
            await db.SaveChangesAsync();
        }
        // Admin+PT must appear only as Admin, not duplicate or inflate PT totals.
        var adminId = trainerIds[^1];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<Fitmate.Domain.Identity.ApplicationUser>>();
            var user = (await db.Users.FindAsync(adminId))!;
            await manager.AddToRoleAsync(user, "Admin");
        }
        trainerIds.Remove(adminId);
        using var readScope = _factory.Services.CreateScope();
        var store = readScope.ServiceProvider.GetRequiredService<IAdminUserStore>();
        var first = await store.ListPagedAsync(new("pt", null, marker, 1, 10, "createdAt:desc"), default);
        var second = await store.ListPagedAsync(new("pt", null, marker, 2, 10, "createdAt:desc"), default);
        first.Total.Should().Be(12);
        second.Total.Should().Be(12);
        first.Items.Should().HaveCount(10);
        second.Items.Should().HaveCount(2);
        first.Items.Concat(second.Items).Select(x => x.Id).Should().BeEquivalentTo(trainerIds);
        first.Items.Concat(second.Items).Should().OnlyContain(x => x.Role == "PT");
        var repeated = await store.ListPagedAsync(new("pt", null, marker, 1, 10, "createdAt:desc"), default);
        repeated.Items.Select(x => x.Id).Should().Equal(first.Items.Select(x => x.Id));
        var empty = await store.ListPagedAsync(new("pt", null, marker, 3, 10, "createdAt:desc"), default);
        empty.Items.Should().BeEmpty();
        empty.Total.Should().Be(12);
    }
}
