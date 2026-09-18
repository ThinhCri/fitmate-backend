using Fitmate.Domain.Common;

namespace Fitmate.Domain.Identity;

/// <summary>
/// User aggregate root. Quản lý profile, status, password, role.
/// </summary>
public class User : AuditableEntity
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public string Timezone { get; private set; } = "Asia/Ho_Chi_Minh";
    public bool EmailVerified { get; private set; }
    public DateTime? LastActiveAt { get; private set; }

    private User() { }

    public static User CreateMember(string email, string passwordHash, string fullName, string timezone = "Asia/Ho_Chi_Minh")
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("email required");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("passwordHash required");
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("fullName required");

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            FullName = fullName,
            Role = UserRole.Member,
            Status = UserStatus.Active,
            Timezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Ho_Chi_Minh" : timezone,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static User CreatePt(string email, string passwordHash, string fullName, string timezone = "Asia/Ho_Chi_Minh")
    {
        var u = CreateMember(email, passwordHash, fullName, timezone);
        u.Role = UserRole.Pt;
        return u;
    }

    public static User CreateAdmin(string email, string passwordHash, string fullName)
    {
        var u = CreateMember(email, passwordHash, fullName);
        u.Role = UserRole.Admin;
        u.EmailVerified = true;
        return u;
    }

    public void ChangePassword(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash)) throw new ArgumentException("newHash required");
        PasswordHash = newHash;
        Touch();
    }

    public void UpdateProfile(string? fullName = null, string? avatarUrl = null, string? timezone = null)
    {
        if (fullName != null) FullName = fullName;
        if (avatarUrl != null) AvatarUrl = avatarUrl;
        if (timezone != null) Timezone = timezone;
        Touch();
    }

    public void MarkEmailVerified()
    {
        EmailVerified = true;
        Touch();
    }

    public void RecordActive()
    {
        LastActiveAt = DateTime.UtcNow;
    }

    public void Lock()
    {
        if (Status == UserStatus.Locked) return;
        Status = UserStatus.Locked;
        Touch();
    }

    public void Unlock()
    {
        if (Status != UserStatus.Locked) return;
        Status = UserStatus.Active;
        Touch();
    }

    public void RequestDeletion()
    {
        Status = UserStatus.PendingDeletion;
        Touch();
    }

    public void ElevateToAdmin()
    {
        // Caller must enforce authorization (admin only).
        Role = UserRole.Admin;
        Touch();
    }
}
