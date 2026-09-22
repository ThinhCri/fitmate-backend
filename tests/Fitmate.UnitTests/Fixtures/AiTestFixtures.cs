using Fitmate.Application.Chat;
using Fitmate.Application.Nutrition;
using Fitmate.Application.Workout;

namespace Fitmate.UnitTests.Fixtures;

/// <summary>
/// BE-817 / ai-contract.md §3: TEST-ONLY fixtures and doubles for AI capabilities.
/// CRITICAL: These fixtures are strictly for automated unit and contract tests.
/// They must NEVER be used as fallbacks in production or to mask AI unavailability.
/// </summary>
public static class AiTestFixtures
{
    public static readonly NutritionScanResultView GoldenNutritionScanResult = new(
        Items: new List<NutritionScanItemView>
        {
            new("Phở bò tái", 550, 28, 65, 18)
        },
        TotalCalories: 550,
        Confidence: 0.88m,
        Source: "test_fixture_only",
        Note: "TEST-ONLY fixture: verified macro breakdown for contract testing.");

    public static readonly WorkoutSuggestionResultView GoldenWorkoutSuggestionResult = new(
        SuggestedPlanName: "Kế hoạch Tăng cơ 3 buổi/tuần",
        Description: "Kế hoạch mẫu dành cho người mới bắt đầu.",
        Exercises: new List<WorkoutSuggestedExerciseView>
        {
            new("Barbell Bench Press", 3, 10, 90, "Khởi động kỹ trước khi nâng."),
            new("Goblet Squat", 3, 12, 60, "Giữ lưng thẳng, hạ đùi song song sàn."),
            new("Dumbbell Row", 3, 12, 60, "Kéo tạ sát thân, siết xô.")
        },
        Confidence: 0.90m,
        Source: "test_fixture_only",
        Disclaimer: "TEST-ONLY fixture: người dùng cần kiểm tra và xác nhận trước khi lưu vào lịch tập.");

    public static readonly CompanionChatResponseView GoldenCompanionChatResponse = new(
        MessageId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ReplyText: "Chào bạn! Hôm nay bạn đã hoàn thành bài tập chân và ghi nhận đủ 2.000 calo. Hãy uống thêm nước và nghỉ ngơi nhé!",
        CreatedAt: new DateTime(2026, 9, 23, 3, 0, 0, DateTimeKind.Utc),
        Source: "test_fixture_only",
        Disclaimer: "TEST-ONLY fixture: AI Companion chỉ mang tính tư vấn thể hình tổng quát, không có quyền thay đổi thông tin tài khoản.");

    public sealed class TestGoldenAiNutritionAdapter : IAiNutritionAdapter
    {
        public Task<NutritionScanResultView> EstimateFromImageAsync(
            Stream imageStream, string contentType, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(GoldenNutritionScanResult);
        }
    }

    public sealed class TestGoldenAiWorkoutAdapter : IAiWorkoutAdapter
    {
        public Task<WorkoutSuggestionResultView> SuggestWorkoutPlanAsync(
            WorkoutSuggestionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(GoldenWorkoutSuggestionResult);
        }
    }

    public sealed class TestGoldenAiCompanionAdapter : IAiCompanionAdapter
    {
        public Task<CompanionChatResponseView> GenerateReplyAsync(
            CompanionChatRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(GoldenCompanionChatResponse);
        }
    }
}

