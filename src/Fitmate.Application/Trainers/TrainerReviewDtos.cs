using System.Text.Json.Serialization;

namespace Fitmate.Application.Trainers;

public sealed record CreateReviewRequest(
    [property: JsonPropertyName("rating")] int Rating,
    [property: JsonPropertyName("text")] string Text);

public sealed record ReviewDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("from_user_id")] Guid FromUserId,
    [property: JsonPropertyName("reviewer_name")] string ReviewerName,
    [property: JsonPropertyName("pt_user_id")] Guid PtUserId,
    [property: JsonPropertyName("rating")] int Rating,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("created_at")] DateTime CreatedAtUtc);

public sealed record TrainerReviewsSummaryDto(
    [property: JsonPropertyName("average_rating")] decimal AverageRating,
    [property: JsonPropertyName("total_reviews")] int TotalReviews,
    [property: JsonPropertyName("reviews")] IReadOnlyList<ReviewDto> Reviews);

