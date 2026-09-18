using Fitmate.Domain.Common;

namespace Fitmate.Domain.Identity;

public class UserProfile : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? Phone { get; private set; }
    public Gender? Gender { get; private set; }
    public DateOnly? Birthday { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? WeightKg { get; private set; }
    public string? FitnessGoal { get; private set; }
    public List<string> TrainingDays { get; private set; } = new();
    public List<string> MedicalConditions { get; private set; } = new();
    public List<string> Allergies { get; private set; } = new();
    public bool MedicalSurveyDone { get; private set; }

    private UserProfile() { }

    public static UserProfile Create(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(
        string? phone = null,
        Gender? gender = null,
        DateOnly? birthday = null,
        decimal? heightCm = null,
        decimal? weightKg = null,
        string? fitnessGoal = null,
        List<string>? trainingDays = null,
        List<string>? medicalConditions = null,
        List<string>? allergies = null)
    {
        if (phone != null) Phone = phone;
        if (gender.HasValue) Gender = gender;
        if (birthday.HasValue) Birthday = birthday;
        if (heightCm.HasValue) HeightCm = heightCm;
        if (weightKg.HasValue) WeightKg = weightKg;
        if (fitnessGoal != null) FitnessGoal = fitnessGoal;
        if (trainingDays != null) TrainingDays = trainingDays;
        if (medicalConditions != null)
        {
            MedicalConditions = medicalConditions;
            MedicalSurveyDone = medicalConditions.Count > 0;
        }
        if (allergies != null) Allergies = allergies;
        Touch();
    }
}
