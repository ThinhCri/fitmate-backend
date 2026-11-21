using System.Security.Claims;
using Fitmate.Api.Auth;
using Fitmate.Api.Trainers;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.UnitTests;

/// <summary>
/// Unit tests & Security checks for PtProfileController (GET/PATCH /api/v1/me/pt-profile).
/// </summary>
public class PtProfileControllerTests
{
    private sealed class InMemoryPtProfileStore : IPtProfileStore
    {
        public Dictionary<Guid, PtProfile> Profiles { get; } = new();

        public Task<PtProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Profiles.GetValueOrDefault(userId));

        public Task AddAsync(PtProfile profile, CancellationToken ct)
        {
            Profiles[profile.UserId] = profile;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUserLookup : IUserRoleLookup
    {
        public HashSet<Guid> Users { get; } = new();
        public HashSet<(Guid userId, string role)> Roles { get; } = new();

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Users.Contains(userId));

        public Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct)
            => Task.FromResult(Roles.Contains((userId, roleName)));

        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<string?>(Roles.FirstOrDefault(r => r.userId == userId).role);
    }

    private sealed class TestCurrentUserAccessor : ICurrentUserAccessor
    {
        private readonly Guid _userId;
        public TestCurrentUserAccessor(Guid userId) => _userId = userId;

        public Guid? GetUserId() => _userId;
        public Guid GetUserIdOrThrow() => _userId;
        public string? GetEmail() => "pt@fitmate.site";
        public string? GetRole() => "PT";
        public bool IsInRole(string role) => role == "PT";
        public ClaimsPrincipal Principal => new();
    }

    [Fact]
    public async Task Get_WhenDraftDoesNotExist_CreatesAndReturnsDraft()
    {
        var ptId = Guid.NewGuid();
        var store = new InMemoryPtProfileStore();
        var users = new FakeUserLookup();
        users.Users.Add(ptId);
        users.Roles.Add((ptId, "PT"));

        var service = new PtProfileService(store, users);
        var controller = new PtProfileController(service);
        var current = new TestCurrentUserAccessor(ptId);

        var actionResult = await controller.Get(current, CancellationToken.None);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)actionResult.Result!;
        var res = ok.Value.Should().BeOfType<PtProfileResponse>().Subject;

        res.UserId.Should().Be(ptId);
        res.Bio.Should().BeNull();
        store.Profiles.Should().ContainKey(ptId);
    }

    [Fact]
    public async Task Update_ValidBioAndSpecialty_Succeeds()
    {
        var ptId = Guid.NewGuid();
        var store = new InMemoryPtProfileStore();
        var users = new FakeUserLookup();
        users.Users.Add(ptId);
        users.Roles.Add((ptId, "PT"));

        var service = new PtProfileService(store, users);
        var controller = new PtProfileController(service);
        var current = new TestCurrentUserAccessor(ptId);

        var updateReq = new UpdatePtProfileRequest(
            Specialty: "Hypertrophy & Fat Loss",
            ExperienceYears: 6,
            PricePerSessionVnd: 400_000,
            Bio: "Certified PT with 6 years experience helping 200+ clients transform."
        );

        var actionResult = await controller.Update(current, updateReq, CancellationToken.None);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)actionResult.Result!;
        var res = ok.Value.Should().BeOfType<PtProfileResponse>().Subject;

        res.Specialty.Should().Be("Hypertrophy & Fat Loss");
        res.ExperienceYears.Should().Be(6);
        res.PricePerSessionVnd.Should().Be(400_000);
        res.Bio.Should().Be("Certified PT with 6 years experience helping 200+ clients transform.");
    }

    [Fact]
    public async Task Update_Security_ScopedToCurrentCaller_CannotModifyOthers()
    {
        var pt1 = Guid.NewGuid();
        var pt2 = Guid.NewGuid();
        var store = new InMemoryPtProfileStore();
        var users = new FakeUserLookup();
        users.Users.Add(pt1);
        users.Roles.Add((pt1, "PT"));
        users.Users.Add(pt2);
        users.Roles.Add((pt2, "PT"));

        var service = new PtProfileService(store, users);
        var controller = new PtProfileController(service);

        // PT1 updates their profile
        var current1 = new TestCurrentUserAccessor(pt1);
        await controller.Update(current1, new UpdatePtProfileRequest(Bio: "PT 1 Bio"), CancellationToken.None);

        // PT2 updates their profile
        var current2 = new TestCurrentUserAccessor(pt2);
        await controller.Update(current2, new UpdatePtProfileRequest(Bio: "PT 2 Bio"), CancellationToken.None);

        // PT1's bio should remain intact
        var profile1 = await service.GetByUserIdAsync(pt1, CancellationToken.None);
        profile1!.Bio.Should().Be("PT 1 Bio");

        var profile2 = await service.GetByUserIdAsync(pt2, CancellationToken.None);
        profile2!.Bio.Should().Be("PT 2 Bio");
    }

    [Fact]
    public async Task Update_InvalidExperienceYears_ThrowsValidationException()
    {
        var ptId = Guid.NewGuid();
        var store = new InMemoryPtProfileStore();
        var users = new FakeUserLookup();
        users.Users.Add(ptId);
        users.Roles.Add((ptId, "PT"));

        var service = new PtProfileService(store, users);
        var controller = new PtProfileController(service);
        var current = new TestCurrentUserAccessor(ptId);

        var badReq = new UpdatePtProfileRequest(ExperienceYears: 99); // 0..70 max

        var act = () => controller.Update(current, badReq, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }
}
