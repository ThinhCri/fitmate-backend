using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;

namespace Fitmate.Domain.Trainers;

public class PtProfile : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? Specialty { get; private set; }
    public int? ExperienceYears { get; private set; }
    public long? PricePerSessionVnd { get; private set; }
    public string? Bio { get; private set; }
    public decimal Rating { get; private set; }
    public int ReviewsCount { get; private set; }
    public PtVerificationStatus VerificationStatus { get; private set; } = PtVerificationStatus.PendingDocuments;
    public string? VerificationReason { get; private set; }
    public DateTime? VerificationReviewedAt { get; private set; }
    public Guid? VerificationReviewedByAdminId { get; private set; }

    private PtProfile() { }

    public static PtProfile CreateDraft(Guid userId, string? specialty, int? experienceYears, long? pricePerSessionVnd, string? bio) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Specialty = specialty,
        ExperienceYears = experienceYears,
        PricePerSessionVnd = pricePerSessionVnd,
        Bio = bio,
        Rating = 0,
        ReviewsCount = 0,
        VerificationStatus = PtVerificationStatus.PendingDocuments,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void MarkPendingReview()
    {
        VerificationStatus = PtVerificationStatus.PendingReview;
        VerificationReason = null;
        Touch();
    }

    public void Approve(Guid adminId)
    {
        VerificationStatus = PtVerificationStatus.Approved;
        VerificationReason = null;
        VerificationReviewedAt = DateTime.UtcNow;
        VerificationReviewedByAdminId = adminId;
        Touch();
    }

    public void RequestMoreInformation(Guid adminId, string reason)
    {
        VerificationStatus = PtVerificationStatus.NeedsMoreInformation;
        VerificationReason = reason;
        VerificationReviewedAt = DateTime.UtcNow;
        VerificationReviewedByAdminId = adminId;
        Touch();
    }

    public void RejectFraud(Guid adminId, string reason)
    {
        VerificationStatus = PtVerificationStatus.RejectedFraud;
        VerificationReason = reason;
        VerificationReviewedAt = DateTime.UtcNow;
        VerificationReviewedByAdminId = adminId;
        Touch();
    }

    public void Update(string? specialty, int? experienceYears, long? pricePerSessionVnd, string? bio)
    {
        Specialty = specialty;
        ExperienceYears = experienceYears;
        PricePerSessionVnd = pricePerSessionVnd;
        Bio = bio;
        Touch();
    }

    public void ApplyRating(decimal rating, int reviewsCount)
    {
        Rating = rating;
        ReviewsCount = reviewsCount;
        Touch();
    }
}

public enum PtVerificationStatus
{
    PendingDocuments = 0,
    PendingReview = 1,
    NeedsMoreInformation = 2,
    Approved = 3,
    RejectedFraud = 4
}
