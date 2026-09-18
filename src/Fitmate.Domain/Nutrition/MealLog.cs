using Fitmate.Domain.Common;

namespace Fitmate.Domain.Nutrition;

public class MealLog : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string FoodName { get; private set; } = null!;
    public string? ImageStorageKey { get; private set; }
    public int Calories { get; private set; }
    public int ProteinG { get; private set; }
    public int CarbsG { get; private set; }
    public int FatG { get; private set; }
    public decimal? PortionSize { get; private set; }
    public MealSource Source { get; private set; }
    public decimal? AiConfidence { get; private set; }
    public bool Confirmed { get; private set; }
    public DateTime LoggedAt { get; private set; }

    private MealLog() { }
    public static MealLog Create(Guid userId, string foodName, string? imageStorageKey,
        int calories, int protein, int carbs, int fat, decimal? portionSize,
        MealSource source, decimal? aiConfidence, bool confirmed, DateTime? loggedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FoodName = foodName,
        ImageStorageKey = imageStorageKey,
        Calories = calories,
        ProteinG = protein,
        CarbsG = carbs,
        FatG = fat,
        PortionSize = portionSize,
        Source = source,
        AiConfidence = aiConfidence,
        Confirmed = confirmed,
        LoggedAt = loggedAt ?? DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(string? foodName, int? calories, int? protein, int? carbs, int? fat, decimal? portionSize, bool? confirmed)
    {
        if (foodName != null) FoodName = foodName;
        if (calories.HasValue) Calories = calories.Value;
        if (protein.HasValue) ProteinG = protein.Value;
        if (carbs.HasValue) CarbsG = carbs.Value;
        if (fat.HasValue) FatG = fat.Value;
        if (portionSize.HasValue) PortionSize = portionSize;
        if (confirmed.HasValue) Confirmed = confirmed.Value;
        Touch();
    }
}
