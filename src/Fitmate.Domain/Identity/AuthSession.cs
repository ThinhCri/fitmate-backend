namespace Fitmate.Domain.Identity;

// Server-owned proof; never populated from a client device label or claims.
public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = "";
    public string AuthenticationMethod { get; set; } = "pwd";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}

public sealed class AdminLoginChallenge
{
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Consumed { get; set; }
}
