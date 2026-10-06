using System.Text.Json;
using System.Text.Json.Nodes;
using Fitmate.Api.Errors;
using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Capabilities;
using Fitmate.Application.Dashboard;
using Fitmate.Application.Finance;
using Fitmate.Application.Identity;
using Fitmate.Application.Notifications;
using Fitmate.Application.Payments;
using Fitmate.Application.Preferences;
using Fitmate.Application.Support;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.UnitTests.Contract;

/// <summary>
/// Phase 2 — DTO Serialization Contract Tests.
/// Validates that wire DTOs produce exact JSON keys expected by OpenAPI specifications
/// and Mobile clients. Ensures no accidental camelCase/snake_case mixing, no exposed
/// entity internals, correct enum string casing, integer money representation,
/// and RFC 7807 ProblemDetails compliance.
/// </summary>
public class DtoSerializationContractTests
{
    private static readonly JsonSerializerOptions DefaultOptions = new();

    private static JsonElement SerializeToElement<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<string> GetPropertyNames(JsonElement element)
    {
        element.ValueKind.Should().Be(JsonValueKind.Object);
        return element.EnumerateObject().Select(p => p.Name).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Auth & Session DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AuthResponse_And_UserSummary_Serialize_With_Exact_Snake_Case_Keys()
    {
        var userSummary = new UserSummaryDto(
            Id: Guid.NewGuid(),
            Email: "member@fitmate.vn",
            AvatarUrl: "https://storage.fitmate.vn/avatars/user.jpg",
            FullName: "Nguyen Van A",
            Role: "member");

        var authResponse = new AuthResponse(
            AccessToken: "jwt-access-token",
            RefreshToken: "refresh-token-opaque",
            ExpiresIn: 900,
            User: userSummary);

        var root = SerializeToElement(authResponse);
        var rootKeys = GetPropertyNames(root);

        rootKeys.Should().BeEquivalentTo(new[]
        {
            "access_token",
            "refresh_token",
            "expires_in",
            "user"
        }, options => options.WithStrictOrdering(),
        "AuthResponse must serialize exact wire keys expected by mobile client and OpenAPI");

        root.GetProperty("access_token").GetString().Should().Be("jwt-access-token");
        root.GetProperty("refresh_token").GetString().Should().Be("refresh-token-opaque");
        root.GetProperty("expires_in").GetInt32().Should().Be(900);

        var userElement = root.GetProperty("user");
        var userKeys = GetPropertyNames(userElement);

        userKeys.Should().BeEquivalentTo(new[]
        {
            "id",
            "email",
            "avatar_url",
            "full_name",
            "role",
            "pt_verification_status"
        }, "UserSummary must have snake_case keys matching OpenAPI schema");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. User Profile DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UserProfileDto_Serializes_With_Exact_Snake_Case_Keys()
    {
        var profile = new UserProfileDto(
            Id: Guid.NewGuid(),
            Email: "member@fitmate.vn",
            FullName: "Tran Thi B",
            AvatarUrl: null,
            Phone: "0901234567",
            Gender: "female",
            Birthday: new DateOnly(1995, 5, 20),
            HeightCm: 165.5m,
            WeightKg: 55.2m,
            FitnessGoal: "weight_loss",
            TrainingDays: new List<string> { "mon", "wed", "fri" },
            MedicalConditions: new List<string>(),
            Allergies: new List<string> { "peanut" },
            Timezone: "Asia/Ho_Chi_Minh",
            Role: "member");

        var element = SerializeToElement(profile);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "id", "email", "full_name", "avatar_url", "phone", "gender",
            "birthday", "height_cm", "weight_kg", "fitness_goal",
            "training_days", "medical_conditions", "allergies", "timezone", "role",
            "bio", "preferred_time_slots", "health_notes"
        });
    }

    [Fact]
    public void PublicUserProfileDto_Serializes_Without_Exposing_Private_Data()
    {
        var publicProfile = new PublicUserProfileDto(
            Id: Guid.NewGuid(),
            FullName: "Le Van C",
            AvatarUrl: "https://storage.fitmate.vn/avatars/pt.jpg",
            Role: "pt",
            FitnessGoal: "muscle_gain",
            CreatedAtUtc: DateTime.UtcNow);

        var element = SerializeToElement(publicProfile);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "id", "full_name", "avatar_url", "role", "fitness_goal", "created_at"
        });

        // Ensure private fields are NOT present
        keys.Should().NotContain("email");
        keys.Should().NotContain("phone");
        keys.Should().NotContain("birthday");
        keys.Should().NotContain("medical_conditions");
        keys.Should().NotContain("allergies");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. System Capabilities DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SystemCapabilitiesDto_Serializes_With_Exact_Snake_Case_Keys()
    {
        var capabilities = new SystemCapabilitiesDto(
            ApiVersion: "1.0.0",
            Environment: "production",
            Ai: new AiCapabilitiesStatus(
                Enabled: false,
                FoodScan: false,
                CompanionChat: false,
                WorkoutAdjust: false,
                StatusReason: "AI capabilities are awaiting artifact delivery (BE-817)"),
            VipPurchase: new FeatureCapabilityStatus(Enabled: false, StatusReason: "VIP purchase not available"),
            RewardCashout: new FeatureCapabilityStatus(Enabled: false, StatusReason: "Reward cashout disabled"),
            PtAutomaticPayout: new FeatureCapabilityStatus(Enabled: false, StatusReason: "Automated payouts disabled"),
            PushNotificationsEnabled: true,
            PaymentsEnabled: true,
            StorageProvider: "local",
            Policies: new PlatformPoliciesSummary(
                PlatformFeeBps: 1000,
                MinCancelHours: 24,
                RefundWindowDays: 7),
            Contact: new PublicContactInfo(
                SupportEmail: "support@fitmate.vn",
                Hotline: "1900-FITMATE",
                TermsUrl: "https://fitmate.vn/terms",
                PrivacyUrl: "https://fitmate.vn/privacy",
                DeletionPolicyUrl: "https://fitmate.vn/account-deletion"));

        var element = SerializeToElement(capabilities);
        var rootKeys = GetPropertyNames(element);

        rootKeys.Should().BeEquivalentTo(new[]
        {
            "api_version",
            "environment",
            "ai",
            "vip_purchase",
            "reward_cashout",
            "pt_automatic_payout",
            "push_notifications_enabled",
            "payments_enabled",
            "storage_provider",
            "policies",
            "contact"
        });

        var aiKeys = GetPropertyNames(element.GetProperty("ai"));
        aiKeys.Should().BeEquivalentTo(new[]
        {
            "enabled", "food_scan", "companion_chat", "workout_adjust", "status_reason"
        });

        var policiesKeys = GetPropertyNames(element.GetProperty("policies"));
        policiesKeys.Should().BeEquivalentTo(new[]
        {
            "platform_fee_bps", "min_cancel_hours", "refund_window_days"
        });

        var contactKeys = GetPropertyNames(element.GetProperty("contact"));
        contactKeys.Should().BeEquivalentTo(new[]
        {
            "support_email", "hotline", "terms_url", "privacy_url", "deletion_policy_url"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Payment DTO
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentView_Serializes_With_Exact_Snake_Case_Keys_And_String_Enum()
    {
        var payment = new PaymentView(
            Id: Guid.NewGuid(),
            AppointmentId: Guid.NewGuid(),
            Method: PaymentMethod.Payos,
            Status: PaymentStatus.Paid,
            AmountVnd: 500000,
            RefundedSoFarVnd: 0,
            RedirectUrl: "https://checkout.payos.vn/payment/123",
            ProviderTxnRef: "PAYOS-TXN-456",
            CreatedAt: new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt: new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc));

        var element = SerializeToElement(payment);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "id",
            "appointment_id",
            "method",
            "status",
            "amount_vnd",
            "refunded_so_far_vnd",
            "redirect_url",
            "provider_txn_ref",
            "created_at",
            "completed_at"
        });

        // Verify status serializes as string, not integer
        element.GetProperty("status").GetString().Should().Be("Paid");
        // Verify money is integer
        element.GetProperty("amount_vnd").GetInt64().Should().Be(500000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Trainer Review DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrainerReviewDtos_Serialize_With_Exact_Snake_Case_Keys()
    {
        var review = new ReviewDto(
            Id: Guid.NewGuid(),
            FromUserId: Guid.NewGuid(),
            ReviewerName: "Học viên A",
            PtUserId: Guid.NewGuid(),
            Rating: 5,
            Text: "Huấn luyện viên rất nhiệt tình và chuyên nghiệp.",
            CreatedAtUtc: DateTime.UtcNow);

        var reviewsPage = new TrainerReviewsSummaryDto(
            AverageRating: 4.8m,
            TotalReviews: 25,
            Reviews: new List<ReviewDto> { review });

        var pageElement = SerializeToElement(reviewsPage);
        var pageKeys = GetPropertyNames(pageElement);

        pageKeys.Should().BeEquivalentTo(new[]
        {
            "average_rating", "total_reviews", "reviews"
        });

        var reviewElement = pageElement.GetProperty("reviews")[0];
        var reviewKeys = GetPropertyNames(reviewElement);

        reviewKeys.Should().BeEquivalentTo(new[]
        {
            "id", "from_user_id", "reviewer_name", "pt_user_id", "rating", "text", "created_at"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Account Deletion DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AccountDeletionStatusDto_Serializes_With_Exact_Snake_Case_Keys()
    {
        var status = new AccountDeletionStatusDto(
            UserId: Guid.NewGuid(),
            Status: "Active",
            IsPendingDeletion: false,
            RequestedAt: null,
            ScheduledPermanentDeletionAt: null,
            RetentionDaysRemaining: 30,
            HasActiveBookings: false);

        var element = SerializeToElement(status);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "user_id",
            "status",
            "is_pending_deletion",
            "requested_at",
            "scheduled_permanent_deletion_at",
            "retention_days_remaining",
            "has_active_bookings"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Platform Policy DTO
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlatformPolicy_Serializes_With_Exact_Snake_Case_Keys()
    {
        var policy = new PlatformPolicy(
            PlatformFeeRateBps: 1000,
            PolicyVersion: "v1",
            EffectiveFrom: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Editable: false,
            DisplayHint: "Platform fee 10% (DEC-051)");

        var element = SerializeToElement(policy);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "platform_fee_rate_bps",
            "policy_version",
            "effective_from",
            "editable",
            "display_hint"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. User Preferences DTO
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UserPreferenceDto_Serializes_With_Exact_Snake_Case_Keys()
    {
        var pref = new UserPreferenceDto(
            UserId: Guid.NewGuid(),
            Language: "vi",
            Theme: "system",
            Timezone: "Asia/Ho_Chi_Minh",
            NotificationsEnabled: true,
            EmailNotifications: true,
            PushNotifications: true,
            HealthDataSharingConsent: false,
            BiometricLoginEnabled: false,
            UpdatedAt: DateTime.UtcNow);

        var element = SerializeToElement(pref);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "user_id",
            "language",
            "theme",
            "timezone",
            "notifications_enabled",
            "email_notifications",
            "push_notifications",
            "health_data_sharing_consent",
            "biometric_login_enabled",
            "updated_at",
            "target_calories",
            "target_protein_g",
            "target_carbs_g",
            "target_fat_g"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. Support Ticket DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SupportTicket_Dtos_Serialize_With_Exact_Snake_Case_Keys()
    {
        var summary = new SupportTicketSummaryDto(
            Id: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Subject: "Lỗi thanh toán",
            Category: "payment",
            Priority: "high",
            Status: "open",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            ResolvedAt: null,
            ClosedAt: null);

        var element = SerializeToElement(summary);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "id", "user_id", "subject", "category", "priority", "status",
            "created_at", "updated_at", "resolved_at", "closed_at"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 10. Dashboard Summary DTO
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardSummaryView_Serializes_With_Exact_Snake_Case_Keys()
    {
        var dashboard = new DashboardSummaryView(
            Date: new DateOnly(2026, 10, 1),
            Nutrition: new NutritionSummaryView(1800, 120, 200, 60, 2200),
            Workout: new WorkoutSummaryView(450, 45, 1),
            NetCalories: 1350,
            Streak: new StreakView(5, 14),
            LatestWeight: new LatestWeightView(68.5m, DateTime.UtcNow),
            UpcomingAppointment: null);

        var element = SerializeToElement(dashboard);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "date", "nutrition", "workout", "net_calories", "streak",
            "latest_weight", "upcoming_appointment", "today_workout", "today_meals"
        });

        var nutritionKeys = GetPropertyNames(element.GetProperty("nutrition"));
        nutritionKeys.Should().BeEquivalentTo(new[]
        {
            "total_calories", "protein_g", "carbs_g", "fat_g", "target_calories",
            "target_protein_g", "target_carbs_g", "target_fat_g"
        });

        var workoutKeys = GetPropertyNames(element.GetProperty("workout"));
        workoutKeys.Should().BeEquivalentTo(new[]
        {
            "total_calories_burned", "duration_minutes", "completed_sessions_count"
        });

        var streakKeys = GetPropertyNames(element.GetProperty("streak"));
        streakKeys.Should().BeEquivalentTo(new[]
        {
            "current_streak_days", "longest_streak_days"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11. Notification DTOs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationView_Serializes_With_Exact_Snake_Case_Keys()
    {
        var notif = new NotificationView(
            Id: Guid.NewGuid(),
            Type: NotificationType.Booking,
            Title: "Lịch hẹn đã được xác nhận",
            Message: "HLV đã xác nhận lịch tập của bạn lúc 14:00.",
            Summary: "Xác nhận lịch tập",
            Payload: null,
            SourceEventId: Guid.NewGuid(),
            SourceType: "booking",
            Read: false,
            CreatedAt: DateTime.UtcNow);

        var element = SerializeToElement(notif);
        var keys = GetPropertyNames(element);

        keys.Should().BeEquivalentTo(new[]
        {
            "id", "type", "title", "message", "summary", "payload",
            "source_event_id", "source_type", "read", "created_at"
        });

        // Enum string serialization
        element.GetProperty("type").GetString().Should().Be("Booking");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 12. RFC 7807 ProblemDetails Compliance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FitmateProblemDetails_Serializes_With_Standard_RFC7807_And_Required_Fields()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["email"] = new[] { "Email is required.", "Email is invalid." },
            ["password"] = new[] { "Password must be at least 12 characters." }
        };

        var problem = FitmateProblemDetails.Create(
            status: 400,
            title: "Validation failed",
            code: Fitmate.Api.Errors.ErrorCodes.ValidationFailed,
            typeSlug: "validation",
            detail: "One or more fields failed validation.",
            traceId: "trace-abc-123",
            errors: errors);

        var element = SerializeToElement(problem);
        var keys = GetPropertyNames(element);

        // Required RFC 7807 + Fitmate custom extensions according to api-contract.md §6
        keys.Should().Contain("type");
        keys.Should().Contain("title");
        keys.Should().Contain("status");
        keys.Should().Contain("detail");
        keys.Should().Contain("instance");
        keys.Should().Contain("code");
        keys.Should().Contain("traceId");
        keys.Should().Contain("errors");

        element.GetProperty("status").GetInt32().Should().Be(400);
        element.GetProperty("code").GetString().Should().Be(Fitmate.Api.Errors.ErrorCodes.ValidationFailed);
        element.GetProperty("traceId").GetString().Should().Be("trace-abc-123");
        element.GetProperty("errors").GetProperty("email").GetArrayLength().Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Money Precision & Formatting Rule (DEC-051)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Money_Values_Must_Be_Integers_In_Vnd_Without_Floating_Point()
    {
        var payment = new PaymentView(
            Id: Guid.NewGuid(),
            AppointmentId: Guid.NewGuid(),
            Method: PaymentMethod.Payos,
            Status: PaymentStatus.Paid,
            AmountVnd: 250000,
            RefundedSoFarVnd: 25000,
            RedirectUrl: null,
            ProviderTxnRef: null,
            CreatedAt: DateTime.UtcNow,
            CompletedAt: DateTime.UtcNow);

        var element = SerializeToElement(payment);

        // amount_vnd must be represented as a 64-bit integer
        element.GetProperty("amount_vnd").ValueKind.Should().Be(JsonValueKind.Number);
        element.GetProperty("amount_vnd").TryGetInt64(out var amount).Should().BeTrue();
        amount.Should().Be(250000);

        // refunded_so_far_vnd must also be an integer
        element.GetProperty("refunded_so_far_vnd").TryGetInt64(out var refunded).Should().BeTrue();
        refunded.Should().Be(25000);
    }
}
