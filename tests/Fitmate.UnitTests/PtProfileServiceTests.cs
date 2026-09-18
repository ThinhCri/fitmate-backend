using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-040: PtProfileService — EnsureDraft / Update / GetByUserId với in-memory store + role lookup.
/// </summary>
public class PtProfileServiceTests
{
    private sealed class InMemoryStore : IPtProfileStore
    {
        public Dictionary<Guid, PtProfile> Items { get; } = new();
        public int SaveCalls { get; private set; }

        public Task<PtProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(userId));

        public Task AddAsync(PtProfile profile, CancellationToken ct)
        {
            Items[profile.UserId] = profile;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserLookup : IUserRoleLookup
    {
        public HashSet<Guid> ExistingUsers { get; } = new();
        public HashSet<(Guid userId, string role)> RoleMembers { get; } = new();

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(ExistingUsers.Contains(userId));

        public Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct)
            => Task.FromResult(RoleMembers.Contains((userId, roleName)));

        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
        {
            if (RoleMembers.Contains((userId, "PT"))) return Task.FromResult<string?>("PT");
            if (RoleMembers.Contains((userId, "Admin"))) return Task.FromResult<string?>("Admin");
            if (RoleMembers.Contains((userId, "Member"))) return Task.FromResult<string?>("Member");
            return Task.FromResult<string?>(null);
        }
    }

    private static PtProfileService NewService(InMemoryStore store, FakeUserLookup users)
        => new(store, users);

    [Fact]
    public async Task EnsureDraft_NewUser_CreatesProfile()
    {
        var store = new InMemoryStore();
        var users = new FakeUserLookup { ExistingUsers = { Guid.NewGuid() } };
        var userId = users.ExistingUsers.First();
        users.RoleMembers.Add((userId, "PT"));

        var profile = await NewService(store, users).EnsureDraftAsync(userId, CancellationToken.None);

        profile.UserId.Should().Be(userId);
        profile.Specialty.Should().BeNull();
        profile.Rating.Should().Be(0);
        store.Items.Should().HaveCount(1);
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnsureDraft_ExistingProfile_ReturnsSame_NoNewRow()
    {
        var store = new InMemoryStore();
        var users = new FakeUserLookup();
        var userId = Guid.NewGuid();
        var existing = PtProfile.CreateDraft(userId, "Yoga", 5, 500_000, null);
        store.Items[userId] = existing;

        var profile = await NewService(store, users).EnsureDraftAsync(userId, CancellationToken.None);

        profile.Should().BeSameAs(existing);
        store.SaveCalls.Should().Be(0); // không save khi đã có
    }

    [Fact]
    public async Task EnsureDraft_UserNotFound_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryStore(), new FakeUserLookup());
        var act = () => svc.EnsureDraftAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>()
            .Where(ex => ex.Code == ErrorCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task EnsureDraft_NotPtRole_ThrowsForbidden()
    {
        var store = new InMemoryStore();
        var users = new FakeUserLookup { ExistingUsers = { Guid.NewGuid() } };
        var userId = users.ExistingUsers.First();
        // user tồn tại nhưng không có role PT

        var act = () => NewService(store, users).EnsureDraftAsync(userId, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(ex => ex.Code == ErrorCodes.Identity.Forbidden);
    }

    [Fact]
    public async Task Update_ValidArgs_UpdatesFields()
    {
        var store = new InMemoryStore();
        var users = new FakeUserLookup();
        var userId = Guid.NewGuid();
        store.Items[userId] = PtProfile.CreateDraft(userId, "Old", 1, 100_000, null);

        var profile = await NewService(store, users).UpdateAsync(userId, "Strength", 7, 800_000, "Coach giỏi", CancellationToken.None);

        profile.Specialty.Should().Be("Strength");
        profile.ExperienceYears.Should().Be(7);
        profile.PricePerSessionVnd.Should().Be(800_000);
        profile.Bio.Should().Be("Coach giỏi");
        store.SaveCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    public async Task Update_BadExperienceYears_Throws(int years)
    {
        var store = new InMemoryStore();
        var userId = Guid.NewGuid();
        store.Items[userId] = PtProfile.CreateDraft(userId, null, null, null, null);

        var act = () => NewService(store, new FakeUserLookup()).UpdateAsync(userId, null, years, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_NegativePrice_Throws()
    {
        var store = new InMemoryStore();
        var userId = Guid.NewGuid();
        store.Items[userId] = PtProfile.CreateDraft(userId, null, null, null, null);

        var act = () => NewService(store, new FakeUserLookup()).UpdateAsync(userId, null, null, -1, null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_BioTooLong_Throws()
    {
        var store = new InMemoryStore();
        var userId = Guid.NewGuid();
        store.Items[userId] = PtProfile.CreateDraft(userId, null, null, null, null);

        var longBio = new string('x', 2001);
        var act = () => NewService(store, new FakeUserLookup()).UpdateAsync(userId, null, null, null, longBio, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_ProfileNotFound_Throws()
    {
        var svc = NewService(new InMemoryStore(), new FakeUserLookup());
        var act = () => svc.UpdateAsync(Guid.NewGuid(), "S", 5, 500_000, null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetByUserId_NotFound_ReturnsNull()
    {
        var svc = NewService(new InMemoryStore(), new FakeUserLookup());
        var p = await svc.GetByUserIdAsync(Guid.NewGuid(), CancellationToken.None);
        p.Should().BeNull();
    }
}
