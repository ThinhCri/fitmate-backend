using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class PtNoShowPenaltyTests
{
    [Fact]
    public void TwoVerifiedIncidents_SuspendOneWeek_ThenOneMonth_ThenBan()
    {
        var pt = PtProfile.CreateDraft(Guid.NewGuid(), "Fitness", 5, 100_000, null);
        var now = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        pt.RecordVerifiedNoShow(now);
        pt.VerifiedNoShowStrikes.Should().Be(1);
        pt.CanAcceptNewBookings(now).Should().BeTrue();
        pt.RecordVerifiedNoShow(now.AddDays(1));
        pt.PenaltyFlags.Should().Be(1);
        pt.SuspendedUntilUtc.Should().Be(now.AddDays(8));
        pt.CanAcceptNewBookings(now.AddDays(2)).Should().BeFalse();
        pt.RecordVerifiedNoShow(now.AddDays(10));
        pt.RecordVerifiedNoShow(now.AddDays(11));
        pt.PenaltyFlags.Should().Be(2);
        pt.SuspendedUntilUtc.Should().Be(now.AddDays(41));
        pt.RecordVerifiedNoShow(now.AddDays(50));
        pt.RecordVerifiedNoShow(now.AddDays(51));
        pt.PermanentlyBanned.Should().BeTrue();
        pt.CanAcceptNewBookings(now.AddYears(2)).Should().BeFalse();
    }

    [Fact]
    public void SecondFlagExpiresAfterOneYearWithoutThirdFlag()
    {
        var pt = PtProfile.CreateDraft(Guid.NewGuid(), "Fitness", 5, 100_000, null);
        var now = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++) pt.RecordVerifiedNoShow(now.AddDays(i));
        var afterYear = now.AddDays(3).AddYears(1).AddDays(1);
        pt.RecordVerifiedNoShow(afterYear);
        pt.VerifiedNoShowStrikes.Should().Be(1);
        pt.PenaltyFlags.Should().Be(0);
        pt.PermanentlyBanned.Should().BeFalse();
    }
}
