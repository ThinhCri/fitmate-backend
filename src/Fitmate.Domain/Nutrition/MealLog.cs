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
    public MealSlot MealSlot { get; private set; }
    public decimal? ServingQuantity { get; private set; }
    public string? ServingUnit { get; private set; }
    public decimal? Grams { get; private set; }
    public string? Notes { get; private set; }
    public MealSource Source { get; private set; }
    public decimal? AiConfidence { get; private set; }
    public bool Confirmed { get; private set; }
    public DateTime LoggedAt { get; private set; }

    private MealLog() { }
    public static MealLog Create(Guid userId, string foodName, string? imageStorageKey,
        int calories, int protein, int carbs, int fat, decimal? portionSize,
        MealSource source, decimal? aiConfidence, bool confirmed, DateTime? loggedAt,
        MealSlot mealSlot = MealSlot.Lunch, decimal? servingQuantity = null,
        string? servingUnit = null, decimal? grams = null, string? notes = null) => new()
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
        MealSlot = mealSlot,
        ServingQuantity = servingQuantity,
        ServingUnit = servingUnit,
        Grams = grams,
        Notes = notes,
        Source = source,
        AiConfidence = aiConfidence,
        Confirmed = confirmed,
        LoggedAt = loggedAt ?? DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(string? foodName, int? calories, int? protein, int? carbs, int? fat,
        decimal? portionSize, bool? confirmed, MealSlot? mealSlot = null,
        decimal? servingQuantity = null, string? servingUnit = null,
        decimal? grams = null, string? notes = null)
    {
        if (foodName != null) FoodName = foodName;
        if (calories.HasValue) Calories = calories.Value;
        if (protein.HasValue) ProteinG = protein.Value;
        if (carbs.HasValue) CarbsG = carbs.Value;
        if (fat.HasValue) FatG = fat.Value;
        if (portionSize.HasValue) PortionSize = portionSize;
        if (confirmed.HasValue) Confirmed = confirmed.Value;
        if (mealSlot.HasValue) MealSlot = mealSlot.Value;
        if (servingQuantity.HasValue) ServingQuantity = servingQuantity.Value;
        if (servingUnit is not null) ServingUnit = servingUnit;
        if (grams.HasValue) Grams = grams.Value;
        if (notes is not null) Notes = notes;
        Touch();
    }
}
