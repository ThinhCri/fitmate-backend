using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

/// <summary>
/// Store cho <see cref="UserModerationAction"/> (BE-211).
/// Dùng cho lock action (set lockout) và warn action (record cho audit).
/// </summary>
public interface IModerationActionStore
{
    /// <summary>BE-211: ghi 1 UserModerationAction row.</summary>
    Task AddAsync(UserModerationAction action, CancellationToken ct);
}
