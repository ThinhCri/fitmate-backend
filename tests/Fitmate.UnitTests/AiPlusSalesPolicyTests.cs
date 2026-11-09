using Fitmate.Application.Ai;
using FluentAssertions;
namespace Fitmate.UnitTests;
public class AiPlusSalesPolicyTests
{
    private static AiOptions ReadyAi() => new() { Enabled = true, CompanionChatEnabled = true, WorkoutAdjustEnabled = true, FoodScanEnabled = false };
    private static AiUsageOptions ReadyUsage() => new() { Enabled = true, FreeChatPerDay = 10, PlusChatPerDay = 100 };
    [Fact] public void ChatOnlySalesDoNotRequireFoodScan() => AiPlusSalesPolicy.CanOpen(ReadyAi(), ReadyUsage(), true).Should().BeTrue();
    [Fact] public void MissingProviderBlocksSales() => AiPlusSalesPolicy.CanOpen(ReadyAi(), ReadyUsage(), false).Should().BeFalse();
    [Fact] public void DisabledQuotaBlocksSales() { var usage = ReadyUsage(); usage.Enabled = false; AiPlusSalesPolicy.CanOpen(ReadyAi(), usage, true).Should().BeFalse(); }
    [Fact] public void PaidLimitMustExceedFreeLimit() { var usage = ReadyUsage(); usage.PlusChatPerDay = 10; AiPlusSalesPolicy.CanOpen(ReadyAi(), usage, true).Should().BeFalse(); }
    [Fact] public void MissingWorkoutCapabilityBlocksPromisedBenefit() { var ai = ReadyAi(); ai.WorkoutAdjustEnabled = false; AiPlusSalesPolicy.CanOpen(ai, ReadyUsage(), true).Should().BeFalse(); }
    [Theory]
    [InlineData(null)] [InlineData("4")] [InlineData("invalid")]
    public void OldClientCannotOpenNewSalesButKeepsRestoreAndEntitlement(string? build)
    {
        var view = new AiSubscriptionView("plus", true, true, "fitmate_ai_plus", "monthly", "active", DateTime.UtcNow.AddDays(10));
        var visible = AiPlusSalesPolicy.ForClient(view, build, 5);
        visible.BillingAvailable.Should().BeFalse();
        visible.RestoreAvailable.Should().BeTrue();
        visible.Plan.Should().Be("plus");
    }
    [Fact] public void UpdatedClientCanSeeOpenStorefront()
    {
        var view = new AiSubscriptionView("free", true, true, "fitmate_ai_plus", "monthly", "none", null);
        AiPlusSalesPolicy.ForClient(view, "5", 5).BillingAvailable.Should().BeTrue();
    }
}

