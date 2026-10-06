using Fitmate.Application.Nutrition;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class NutritionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private sealed class InMemoryNutritionStore : INutritionStore
    {
        public List<MealLog> Meals { get; } = new();
        public MealSchedule? Schedule { get; set; }
        public List<WeeklyMealPlanItem> PlanItems { get; } = new();
        public List<WeeklyMealPlanConsumption> Consumptions { get; } = new();

        public Task AddMealAsync(MealLog meal, CancellationToken ct)
        {
            Meals.Add(meal);
            return Task.CompletedTask;
        }

        public Task DeleteMealAsync(MealLog meal, CancellationToken ct)
        {
            meal.SoftDelete();
            return Task.CompletedTask;
        }

        public Task AddScheduleAsync(MealSchedule schedule, CancellationToken ct)
        {
            Schedule = schedule;
            return Task.CompletedTask;
        }

        public Task<MealLog?> FindMealByIdAsync(Guid mealId, CancellationToken ct)
            => Task.FromResult(Meals.FirstOrDefault(m => m.Id == mealId && !m.IsDeleted));

        public Task<IReadOnlyList<MealLog>> ListMealsAsync(
            Guid userId, DateTime? from, DateTime? to, CancellationToken ct)
        {
            var q = Meals.Where(m => m.UserId == userId && !m.IsDeleted);
            if (from.HasValue) q = q.Where(m => m.LoggedAt >= from.Value);
            if (to.HasValue) q = q.Where(m => m.LoggedAt <= to.Value);
            return Task.FromResult<IReadOnlyList<MealLog>>(
                q.OrderByDescending(m => m.LoggedAt).ToList());
        }

        public Task<MealSchedule?> FindScheduleByUserAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Schedule != null && !Schedule.IsDeleted && Schedule.UserId == userId ? Schedule : null);

        public Task<IReadOnlyList<WeeklyMealPlanItem>> ListWeeklyPlanItemsAsync(
            Guid userId, DateOnly weekStart, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WeeklyMealPlanItem>>(PlanItems
                .Where(x => x.UserId == userId && x.WeekStart == weekStart && !x.IsDeleted).ToList());

        public Task<WeeklyMealPlanItem?> FindWeeklyPlanItemAsync(Guid itemId, CancellationToken ct)
            => Task.FromResult(PlanItems.FirstOrDefault(x => x.Id == itemId && !x.IsDeleted));

        public Task AddWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct)
        {
            PlanItems.Add(item);
            return Task.CompletedTask;
        }

        public Task DeleteWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct)
        {
            item.SoftDelete();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WeeklyMealPlanConsumption>> ListWeeklyPlanConsumptionsAsync(
            Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WeeklyMealPlanConsumption>>(Consumptions
                .Where(x => x.UserId == userId && x.ConsumedDate >= from && x.ConsumedDate < toExclusive && !x.IsDeleted).ToList());

        public Task<WeeklyMealPlanConsumption?> FindWeeklyPlanConsumptionAsync(
            Guid planItemId, DateOnly date, CancellationToken ct)
            => Task.FromResult(Consumptions.FirstOrDefault(x => x.PlanItemId == planItemId && x.ConsumedDate == date && !x.IsDeleted));

        public Task AddWeeklyPlanConsumptionAsync(WeeklyMealPlanConsumption consumption, CancellationToken ct)
        {
            Consumptions.Add(consumption);
            return Task.CompletedTask;
        }

        public Task<bool> TryAddMealConsumptionAsync(
            MealLog meal, WeeklyMealPlanConsumption consumption, CancellationToken ct)
        {
            if (Consumptions.Any(x => x.PlanItemId == consumption.PlanItemId && x.ConsumedDate == consumption.ConsumedDate))
                return Task.FromResult(false);
            Meals.Add(meal);
            Consumptions.Add(consumption);
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubAiAdapter : IAiNutritionAdapter
    {
        public Task<NutritionScanResultView> EstimateFromImageAsync(
            Stream imageStream, string contentType, CancellationToken ct)
            => Task.FromResult(new NutritionScanResultView(
                new List<NutritionScanItemView>
                {
                    new("Rice", 200, 4, 45, 1)
                },
                200, 0.9m, "ai_estimate", null));
    }

    private sealed class TestOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private static NutritionService Sut(
        InMemoryNutritionStore store,
        IAiNutritionAdapter? ai = null,
        IOutboxDispatcher? outbox = null)
        => new(store, ai ?? new StubAiAdapter(), outbox ?? new TestOutbox());

    [Fact]
    public async Task ListMeals_returns_empty_when_no_meals()
    {
        var store = new InMemoryNutritionStore();
        var result = await Sut(store).ListMealsAsync(UserId, null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListMeals_returns_user_meals_ordered_by_loggedAt()
    {
        var store = new InMemoryNutritionStore();
        var m1 = MealLog.Create(UserId, "Breakfast", null, 300, 10, 40, 5, null, MealSource.Manual, null, false, DateTime.UtcNow.AddDays(-2));
        var m2 = MealLog.Create(UserId, "Lunch", null, 500, 20, 60, 10, null, MealSource.Manual, null, false, DateTime.UtcNow.AddDays(-1));
        var m3 = MealLog.Create(UserId, "Dinner", null, 400, 15, 50, 8, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.AddRange(new[] { m1, m2, m3 });

        var result = await Sut(store).ListMealsAsync(UserId, null, null);

        result.Should().HaveCount(3);
        result[0].FoodName.Should().Be("Dinner");
        result[1].FoodName.Should().Be("Lunch");
        result[2].FoodName.Should().Be("Breakfast");
    }

    [Fact]
    public async Task ListMeals_respects_date_filter()
    {
        var store = new InMemoryNutritionStore();
        var old = MealLog.Create(UserId, "Old", null, 100, 5, 20, 2, null, MealSource.Manual, null, false, DateTime.UtcNow.AddDays(-10));
        var recent = MealLog.Create(UserId, "Recent", null, 200, 8, 30, 4, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.AddRange(new[] { old, recent });

        var from = DateTime.UtcNow.AddDays(-5);
        var result = await Sut(store).ListMealsAsync(UserId, from, null);

        result.Should().HaveCount(1);
        result[0].FoodName.Should().Be("Recent");
    }

    [Fact]
    public async Task LogMeal_creates_and_returns_meal_view_and_emits_outbox()
    {
        var store = new InMemoryNutritionStore();
        var outbox = new TestOutbox();
        var request = new LogMealRequest
        {
            FoodName = "Oatmeal",
            Calories = 250,
            ProteinG = 8,
            CarbsG = 42,
            FatG = 5,
            Source = MealSource.Manual
        };

        var result = await Sut(store, outbox: outbox).LogMealAsync(UserId, request);

        result.FoodName.Should().Be("Oatmeal");
        result.Calories.Should().Be(250);
        result.ProteinG.Should().Be(8);
        result.Confirmed.Should().BeTrue();
        store.Meals.Should().HaveCount(1);
        outbox.Messages.Should().ContainSingle(m => m.Type == "nutrition.meal_logged");
    }

    [Fact]
    public async Task LogMeal_throws_validation_on_negative_or_invalid_values()
    {
        var store = new InMemoryNutritionStore();
        var request = new LogMealRequest
        {
            FoodName = "",
            Calories = -10,
            Source = MealSource.Manual
        };

        var act = () => Sut(store).LogMealAsync(UserId, request);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateMeal_updates_and_returns_updated_view_and_emits_outbox()
    {
        var store = new InMemoryNutritionStore();
        var outbox = new TestOutbox();
        var meal = MealLog.Create(UserId, "Original", null, 100, 5, 20, 2, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.Add(meal);

        var result = await Sut(store, outbox: outbox).UpdateMealAsync(
            UserId, meal.Id,
            new UpdateMealRequest { FoodName = "Updated", Calories = 200, Confirmed = true });

        result.Should().NotBeNull();
        result!.FoodName.Should().Be("Updated");
        result.Calories.Should().Be(200);
        result.Confirmed.Should().BeTrue();
        outbox.Messages.Should().ContainSingle(m => m.Type == "nutrition.meal_updated");
    }

    [Fact]
    public async Task UpdateMeal_returns_null_when_not_owner()
    {
        var store = new InMemoryNutritionStore();
        var meal = MealLog.Create(OtherId, "Other meal", null, 100, 5, 20, 2, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.Add(meal);

        var result = await Sut(store).UpdateMealAsync(
            UserId, meal.Id,
            new UpdateMealRequest { FoodName = "Hacked" });

        result.Should().BeNull();
        meal.FoodName.Should().Be("Other meal");
    }

    [Fact]
    public async Task DeleteMeal_soft_deletes_meal_and_emits_outbox()
    {
        var store = new InMemoryNutritionStore();
        var outbox = new TestOutbox();
        var meal = MealLog.Create(UserId, "To Delete", null, 150, 5, 20, 2, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.Add(meal);

        await Sut(store, outbox: outbox).DeleteMealAsync(UserId, meal.Id);

        meal.IsDeleted.Should().BeTrue();
        outbox.Messages.Should().ContainSingle(m => m.Type == "nutrition.meal_deleted");
    }

    [Fact]
    public async Task DeleteMeal_throws_not_found_when_not_owner()
    {
        var store = new InMemoryNutritionStore();
        var meal = MealLog.Create(OtherId, "Other meal", null, 100, 5, 20, 2, null, MealSource.Manual, null, false, DateTime.UtcNow);
        store.Meals.Add(meal);

        var act = () => Sut(store).DeleteMealAsync(UserId, meal.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetSchedule_returns_blank_when_none_exists()
    {
        var store = new InMemoryNutritionStore();
        var result = await Sut(store).GetScheduleAsync(UserId);
        result.Id.Should().Be(Guid.Empty);
        result.Breakfast.Should().BeNull();
    }

    [Fact]
    public async Task UpsertSchedule_creates_then_updates()
    {
        var store = new InMemoryNutritionStore();
        var createReq = new UpsertScheduleRequest { Breakfast = "Eggs", Lunch = "Salad" };
        var created = await Sut(store).UpsertScheduleAsync(UserId, createReq);
        created.Breakfast.Should().Be("Eggs");
        store.Schedule.Should().NotBeNull();

        var updateReq = new UpsertScheduleRequest { Breakfast = "Oatmeal", Dinner = "Fish" };
        var updated = await Sut(store).UpsertScheduleAsync(UserId, updateReq);
        updated.Breakfast.Should().Be("Oatmeal");
        updated.Dinner.Should().Be("Fish");
        updated.Lunch.Should().BeNull();
    }

    [Fact]
    public async Task Weekly_plan_crud_and_consume_creates_one_real_meal()
    {
        var store = new InMemoryNutritionStore();
        var monday = new DateOnly(2026, 10, 5);
        var request = new UpsertWeeklyMealPlanItemRequest
        {
            DayOfWeek = 0,
            MealSlot = MealSlot.Breakfast,
            FoodName = "Yến mạch",
            ServingQuantity = 1,
            ServingUnit = "tô",
            Calories = 320,
            ProteinG = 12,
            CarbsG = 50,
            FatG = 8
        };

        var created = await Sut(store).CreateWeeklyPlanItemAsync(UserId, request, monday);
        created.IsConsumed.Should().BeFalse();

        var consumed = await Sut(store).ConsumeWeeklyPlanItemAsync(UserId, created.Id, monday);
        var replay = await Sut(store).ConsumeWeeklyPlanItemAsync(UserId, created.Id, monday);

        consumed.Should().NotBeNull();
        replay!.Id.Should().Be(consumed!.Id);
        store.Meals.Should().ContainSingle();
        store.Consumptions.Should().ContainSingle();
        var weekly = await Sut(store).ListWeeklyPlanAsync(UserId, monday);
        weekly.Should().ContainSingle(x => x.IsConsumed && x.MealLogId == consumed.Id);
    }

    [Fact]
    public async Task Consume_weekly_plan_rejects_a_date_for_another_weekday()
    {
        var store = new InMemoryNutritionStore();
        var monday = new DateOnly(2026, 10, 5);
        var created = await Sut(store).CreateWeeklyPlanItemAsync(UserId,
            new UpsertWeeklyMealPlanItemRequest
            {
                DayOfWeek = 0,
                MealSlot = MealSlot.Lunch,
                FoodName = "Cơm gà",
                Calories = 500
            }, monday);

        var act = () => Sut(store).ConsumeWeeklyPlanItemAsync(UserId, created.Id, monday.AddDays(1));
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ScanMeal_stub_throws_DomainException_with_correct_code()
    {
        var store = new InMemoryNutritionStore();
        var stub = new AiNutritionStubAdapter();
        var sut = new NutritionService(store, stub, new TestOutbox());
        using var stream = new MemoryStream(new byte[1]);

        var act = () => sut.ScanMealAsync(UserId, stream, "image/jpeg");

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Code == ErrorCodes.AI.Unavailable && e.HttpStatus == 503);
    }
}
