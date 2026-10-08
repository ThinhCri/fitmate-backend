using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class PtServiceDetailsTests
{
    [Fact]
    public void InPersonService_RequiresFixedLocation()
    {
        var service = Service.Create(Guid.NewGuid(), "Strength coaching", "Individual workout with technique review.", 300_000, 60);

        var configure = () => service.ConfigureDetails(ServiceKind.OneToOne, ServiceDeliveryMode.InPerson,
            "Beginners", "Training shoes", "Technique coaching", null, null);

        configure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OnlineService_IsSavedAsInactiveDraft()
    {
        var service = Service.Create(Guid.NewGuid(), "Online assessment", "Assessment and four-week workout plan.", 300_000, 60);

        service.ConfigureDetails(ServiceKind.AssessmentAndPlan, ServiceDeliveryMode.Online,
            "Beginners", "Camera and training shoes", "Four-week plan", null, null);

        service.IsActive.Should().BeFalse();
        service.LocationName.Should().BeNull();
        service.Kind.Should().Be(ServiceKind.AssessmentAndPlan);
    }
}
