using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class PtVerificationFlowTests
{
    [Fact]
    public void VerificationFlow_TracksReviewOutcome()
    {
        var adminId = Guid.NewGuid();
        var profile = PtProfile.CreateDraft(Guid.NewGuid(), "Strength", 3, 300_000, null);

        profile.VerificationStatus.Should().Be(PtVerificationStatus.PendingDocuments);
        profile.MarkPendingReview();
        profile.VerificationStatus.Should().Be(PtVerificationStatus.PendingReview);

        profile.RequestMoreInformation(adminId, "Ảnh chứng chỉ chưa rõ số hiệu");
        profile.VerificationStatus.Should().Be(PtVerificationStatus.NeedsMoreInformation);
        profile.VerificationReason.Should().Contain("chưa rõ");

        profile.MarkPendingReview();
        profile.Approve(adminId);
        profile.VerificationStatus.Should().Be(PtVerificationStatus.Approved);
        profile.VerificationReviewedByAdminId.Should().Be(adminId);
    }

    [Fact]
    public void RejectFraud_MarksApplicationAsFraud()
    {
        var adminId = Guid.NewGuid();
        var profile = PtProfile.CreateDraft(Guid.NewGuid(), "Yoga", 2, 250_000, null);

        profile.RejectFraud(adminId, "Đơn vị cấp xác nhận chứng chỉ giả mạo");

        profile.VerificationStatus.Should().Be(PtVerificationStatus.RejectedFraud);
        profile.VerificationReason.Should().Contain("giả mạo");
    }
}
