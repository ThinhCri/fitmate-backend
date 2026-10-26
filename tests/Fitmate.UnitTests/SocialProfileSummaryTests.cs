using Fitmate.Application.Identity;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class SocialProfileSummaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Summary_UsesActualCountsAndNeverLeaksAnotherUsersWorkoutCount(bool self)
    {
        var viewer = Guid.NewGuid(); var target = self ? viewer : Guid.NewGuid();
        var profiles = new Mock<IUserProfileService>(); var blocks = new Mock<IBlockStore>(); var store = new Mock<ISocialProfileSummaryStore>();
        profiles.Setup(x => x.GetPublicProfileAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync((PublicUserProfileDto)null!);
        store.Setup(x => x.ReadAsync(viewer, target, It.IsAny<CancellationToken>())).ReturnsAsync((10, 3, (int?)99));
        var result = await new SocialProfileSummaryService(profiles.Object, blocks.Object, store.Object).GetAsync(viewer, target, default);
        result.PostCount.Should().Be(10); result.FriendCount.Should().Be(3);
        result.CompletedWorkoutsCount.Should().Be(self ? 99 : null);
        result.Badges.Should().ContainSingle().Which.Id.Should().Be("community-ten-visible");
    }

    [Fact]
    public async Task BlockedProfile_IsNotFoundAndDoesNotReadActivity()
    {
        var viewer = Guid.NewGuid(); var target = Guid.NewGuid();
        var blocks = new Mock<IBlockStore>(); var store = new Mock<ISocialProfileSummaryStore>();
        blocks.Setup(x => x.IsBlockedEitherWayAsync(viewer, target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var act = () => new SocialProfileSummaryService(Mock.Of<IUserProfileService>(), blocks.Object, store.Object).GetAsync(viewer, target, default);
        await act.Should().ThrowAsync<NotFoundException>();
        store.Verify(x => x.ReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
