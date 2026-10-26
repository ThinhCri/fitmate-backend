using Fitmate.Application.Ai;
using Fitmate.Application.Auth;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class WorkoutSuggestionServiceTests
{
    [Fact]
    public async Task ModelFailure_ReleasesQuotaAndNeverCommitsUsage()
    {
        var user = Guid.NewGuid(); var reservation = new AiUsageReservation(Guid.NewGuid(), true);
        var auth = new Mock<IAuthorizationGuard>(); var ai = new Mock<IAiWorkoutAdapter>(); var usage = new Mock<IAiUsageService>();
        usage.Setup(x => x.ReserveAsync(user, "chat", It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        ai.Setup(x => x.SuggestWorkoutPlanAsync(It.IsAny<WorkoutSuggestionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new AiUnavailableException());
        var act = () => new WorkoutSuggestionService(auth.Object, ai.Object, usage.Object).SuggestAsync(new(user, "mục tiêu", "mới"), default);
        await act.Should().ThrowAsync<AiUnavailableException>();
        usage.Verify(x => x.ReleaseAsync(reservation, It.IsAny<CancellationToken>()), Times.Once);
        usage.Verify(x => x.CommitAsync(It.IsAny<AiUsageReservation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidInput_DoesNotReserveQuotaOrCallProvider()
    {
        var usage = new Mock<IAiUsageService>(); var ai = new Mock<IAiWorkoutAdapter>();
        var act = () => new WorkoutSuggestionService(Mock.Of<IAuthorizationGuard>(), ai.Object, usage.Object).SuggestAsync(new(Guid.NewGuid(), "", "mới"), default);
        await act.Should().ThrowAsync<ValidationException>();
        usage.Verify(x => x.ReserveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        ai.Verify(x => x.SuggestWorkoutPlanAsync(It.IsAny<WorkoutSuggestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
