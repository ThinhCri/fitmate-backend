using System.Text.Json.Serialization;

namespace Fitmate.Application.Identity;

public sealed record UserProfileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("birthday")] DateOnly? Birthday,
    [property: JsonPropertyName("height_cm")] decimal? HeightCm,
    [property: JsonPropertyName("weight_kg")] decimal? WeightKg,
    [property: JsonPropertyName("fitness_goal")] string? FitnessGoal,
    [property: JsonPropertyName("training_days")] List<string> TrainingDays,
    [property: JsonPropertyName("medical_conditions")] List<string> MedicalConditions,
    [property: JsonPropertyName("allergies")] List<string> Allergies,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("bio")] string? Bio = null,
    [property: JsonPropertyName("preferred_time_slots")] List<string>? PreferredTimeSlots = null,
    [property: JsonPropertyName("health_notes")] string? HealthNotes = null);

public sealed record UserProfileUpdateRequest(
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("birthday")] DateOnly? Birthday,
    [property: JsonPropertyName("height_cm")] decimal? HeightCm,
    [property: JsonPropertyName("weight_kg")] decimal? WeightKg,
    [property: JsonPropertyName("fitness_goal")] string? FitnessGoal,
    [property: JsonPropertyName("training_days")] List<string>? TrainingDays,
    [property: JsonPropertyName("medical_conditions")] List<string>? MedicalConditions,
    [property: JsonPropertyName("allergies")] List<string>? Allergies,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("bio")] string? Bio = null,
    [property: JsonPropertyName("preferred_time_slots")] List<string>? PreferredTimeSlots = null,
    [property: JsonPropertyName("health_notes")] string? HealthNotes = null);

public sealed record PublicUserProfileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("fitness_goal")] string? FitnessGoal,
    [property: JsonPropertyName("created_at")] DateTime CreatedAtUtc);

