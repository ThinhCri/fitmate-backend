using Fitmate.Api.Capabilities;
using Fitmate.Application.Ai;
using Fitmate.Application.Capabilities;
using Fitmate.Application.Chat;
using Fitmate.Application.Nutrition;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Fitmate.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-817 PREPARE NOW: Contract, validation, cancellation, and unavailable error tests for AI capability adapters.
/// </summary>
public class AiAdapterTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Fitmate.Api";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task AiNutritionStubAdapter_ThrowsUnavailable_WithHttpStatus503_WhenDisabled()
    {
        var options = Options.Create(new AiOptions { Enabled = false, FoodScanEnabled = false });
        var adapter = new AiNutritionStubAdapter(options);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var act = () => adapter.EstimateFromImageAsync(stream, "image/jpeg", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiUnavailableException>();
        ex.Which.HttpStatus.Should().Be(503);
        ex.Which.Code.Should().Be(ErrorCodes.AI.Unavailable);
    }

    [Fact]
    public async Task AiNutritionStubAdapter_ThrowsOperationCanceledException_WhenCanceled()
    {
        var adapter = new AiNutritionStubAdapter();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => adapter.EstimateFromImageAsync(stream, "image/jpeg", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AiNutritionStubAdapter_ThrowsValidationException_WhenStreamIsEmpty()
    {
        var adapter = new AiNutritionStubAdapter();
        using var emptyStream = new MemoryStream();

        var act = () => adapter.EstimateFromImageAsync(emptyStream, "image/jpeg", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.FieldInvalid);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/pdf")]
    [InlineData("image/gif")]
    [InlineData("")]
    public async Task AiNutritionStubAdapter_ThrowsValidationException_WhenContentTypeIsInvalid(string invalidType)
    {
        var adapter = new AiNutritionStubAdapter();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var act = () => adapter.EstimateFromImageAsync(stream, invalidType, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.FieldInvalid);
    }

    [Fact]
    public async Task AiWorkoutStubAdapter_ThrowsUnavailable_WithHttpStatus503_WhenDisabled()
    {
        var options = Options.Create(new AiOptions { Enabled = false, WorkoutAdjustEnabled = false });
        var adapter = new AiWorkoutStubAdapter(options);
        var req = new WorkoutSuggestionRequest(Guid.NewGuid(), "Weight loss", "Beginner");

        var act = () => adapter.SuggestWorkoutPlanAsync(req, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiUnavailableException>();
        ex.Which.HttpStatus.Should().Be(503);
        ex.Which.Code.Should().Be(ErrorCodes.AI.Unavailable);
    }

    [Fact]
    public async Task AiWorkoutStubAdapter_ThrowsOperationCanceledException_WhenCanceled()
    {
        var adapter = new AiWorkoutStubAdapter();
        var req = new WorkoutSuggestionRequest(Guid.NewGuid(), "Hypertrophy", "Intermediate");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => adapter.SuggestWorkoutPlanAsync(req, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AiWorkoutStubAdapter_ThrowsValidationException_WhenUserIdIsEmpty()
    {
        var adapter = new AiWorkoutStubAdapter();
        var req = new WorkoutSuggestionRequest(Guid.Empty, "Endurance", "Advanced");

        var act = () => adapter.SuggestWorkoutPlanAsync(req, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task AiCompanionStubAdapter_ThrowsUnavailable_WithHttpStatus503_WhenDisabled()
    {
        var options = Options.Create(new AiOptions { Enabled = false, CompanionChatEnabled = false });
        var adapter = new AiCompanionStubAdapter(options);
        var req = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), "How should I warm up today?");

        var act = () => adapter.GenerateReplyAsync(req, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiUnavailableException>();
        ex.Which.HttpStatus.Should().Be(503);
        ex.Which.Code.Should().Be(ErrorCodes.AI.Unavailable);
    }

    [Fact]
    public async Task AiCompanionStubAdapter_ThrowsOperationCanceledException_WhenCanceled()
    {
        var adapter = new AiCompanionStubAdapter();
        var req = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), "Hello coach!");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => adapter.GenerateReplyAsync(req, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AiCompanionStubAdapter_ThrowsValidationException_WhenMessageIsEmpty(string emptyMessage)
    {
        var adapter = new AiCompanionStubAdapter();
        var req = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), emptyMessage);

        var act = () => adapter.GenerateReplyAsync(req, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task AiCompanionStubAdapter_ThrowsValidationException_WhenMessageTooLong()
    {
        var adapter = new AiCompanionStubAdapter();
        var longMessage = new string('A', 2001);
        var req = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), longMessage);

        var act = () => adapter.GenerateReplyAsync(req, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.TooLong);
    }

    [Fact]
    public void SystemCapabilitiesController_ReflectsAiOptions_WhenDisabled()
    {
        var env = new TestWebHostEnvironment { EnvironmentName = "Production" };
        var options = Options.Create(new AiOptions
        {
            Enabled = false,
            FoodScanEnabled = false,
            CompanionChatEnabled = false,
            WorkoutAdjustEnabled = false
        });

        var controller = new SystemCapabilitiesController(env, options);
        var result = controller.GetCapabilities() as OkObjectResult;

        result.Should().NotBeNull();
        var dto = result!.Value as SystemCapabilitiesDto;
        dto.Should().NotBeNull();
        dto!.Ai.Enabled.Should().BeFalse();
        dto.PushNotificationsEnabled.Should().BeFalse();
        dto.Contact.PrivacyUrl.Should().Be("https://fitmate.site/privacy/");
        dto.Contact.TermsUrl.Should().Be("https://fitmate.site/terms/");
        dto.Contact.Hotline.Should().BeEmpty();
        dto.Ai.FoodScan.Should().BeFalse();
        dto.Ai.CompanionChat.Should().BeFalse();
        dto.Ai.WorkoutAdjust.Should().BeFalse();
        dto.Ai.StatusReason.Should().Contain("awaiting artifact delivery and validation (BE-817)");
    }

    [Fact]
    public void SystemCapabilitiesController_ReflectsAiOptions_WhenEnabled()
    {
        var env = new TestWebHostEnvironment { EnvironmentName = "Development" };
        var options = Options.Create(new AiOptions
        {
            Enabled = true,
            FoodScanEnabled = true,
            CompanionChatEnabled = false,
            WorkoutAdjustEnabled = false
        });

        var controller = new SystemCapabilitiesController(env, options);
        var result = controller.GetCapabilities() as OkObjectResult;

        result.Should().NotBeNull();
        var dto = result!.Value as SystemCapabilitiesDto;
        dto.Should().NotBeNull();
        dto!.Ai.Enabled.Should().BeTrue();
        dto.Ai.FoodScan.Should().BeTrue();
        dto.Ai.CompanionChat.Should().BeFalse();
        dto.Ai.WorkoutAdjust.Should().BeFalse();
        dto.Ai.StatusReason.Should().Be("AI capabilities are active.");
    }

    [Fact]
    public void AiTestFixtures_GoldenFixtures_HaveExpectedContracts()
    {
        AiTestFixtures.GoldenNutritionScanResult.Source.Should().Be("test_fixture_only");
        AiTestFixtures.GoldenNutritionScanResult.TotalCalories.Should().Be(550);
        AiTestFixtures.GoldenNutritionScanResult.Confidence.Should().Be(0.88m);

        AiTestFixtures.GoldenWorkoutSuggestionResult.Source.Should().Be("test_fixture_only");
        AiTestFixtures.GoldenWorkoutSuggestionResult.Exercises.Should().HaveCount(3);

        AiTestFixtures.GoldenCompanionChatResponse.Source.Should().Be("test_fixture_only");
        AiTestFixtures.GoldenCompanionChatResponse.ReplyText.Should().NotBeNullOrWhiteSpace();
    }
}

