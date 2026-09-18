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
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(string? specialty, int? experienceYears, long? pricePerSessionVnd, string? bio)
    {
        Specialty = specialty;
        ExperienceYears = experienceYears;
        PricePerSessionVnd = pricePerSessionVnd;
        Bio = bio;
        Touch();
    }

    public void ApplyRating(int rating, int reviewsCount)
    {
        Rating = rating;
        ReviewsCount = reviewsCount;
        Touch();
    }
}
