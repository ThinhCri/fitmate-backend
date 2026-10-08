using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public sealed class ManualRefundApprovalTests
{
    [Fact]
    public void Approval_starts_a_continuous_48_hour_transfer_deadline()
    {
        var refund = Refund.Create(Guid.NewGuid(), Guid.NewGuid(), 125_000, "Member cancellation", Guid.NewGuid());
        var approvedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        refund.Approve(Guid.NewGuid(), approvedAt);

        refund.Status.Should().Be(RefundStatus.Pending);
        refund.TransferDueAtUtc.Should().Be(approvedAt.AddHours(48));
        refund.SucceededAtUtc.Should().BeNull();
    }

    [Fact]
    public void Manual_transfer_requires_approval_and_a_real_utc_transfer_time()
    {
        var refund = Refund.Create(Guid.NewGuid(), Guid.NewGuid(), 125_000, "Member cancellation", Guid.NewGuid());
        var approvedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        Action beforeApproval = () => refund.MarkManualTransferSucceeded("BANK-01", approvedAt);
        beforeApproval.Should().Throw<ConflictException>();

        refund.Approve(Guid.NewGuid(), approvedAt);
        Action beforeActualTransfer = () => refund.MarkManualTransferSucceeded("BANK-01", approvedAt.AddMinutes(-1));
        beforeActualTransfer.Should().Throw<ConflictException>();

        refund.MarkManualTransferSucceeded("BANK-01", approvedAt.AddHours(1));
        refund.Status.Should().Be(RefundStatus.Succeeded);
        refund.ProviderRefundRef.Should().Be("BANK-01");
        refund.SucceededAtUtc.Should().Be(approvedAt.AddHours(1));
    }
}
