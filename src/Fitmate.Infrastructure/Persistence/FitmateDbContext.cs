using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Outbox;
using Fitmate.Domain.Social;
using Fitmate.Domain.Moderation;
using Fitmate.Domain.Chat;
using Fitmate.Domain.Workout;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Review;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Persistence;

/// <summary>
/// DbContext chính của Fitmate. Theo DEC-032, kế thừa <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// để quản lý schema Identity (users, roles, user_roles, user_claims, role_claims, user_tokens, user_logins)
/// cùng các entity nghiệp vụ khác.
/// </summary>
public class FitmateDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public FitmateDbContext(DbContextOptions<FitmateDbContext> options) : base(options) { }

    // Identity aggregates - IdentityDbContext đã cung cấp Users, Roles, UserRoles,
    // UserClaims, RoleClaims, UserTokens, UserLogins. Thêm alias để code nghiệp vụ không cần đổi.
    public new DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public new DbSet<IdentityRole<Guid>> Roles => Set<IdentityRole<Guid>>();

    // Identity user management.
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Trainers
    public DbSet<PtProfile> PtProfiles => Set<PtProfile>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    // Booking
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<BookingHold> BookingHolds => Set<BookingHold>();

    // Payment
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();

    // Outbox
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    // Social
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();

    // Moderation
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<UserModerationAction> UserModerationActions => Set<UserModerationAction>();

    // Chat
    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // Workout
    public DbSet<WorkoutPlan> WorkoutPlans => Set<WorkoutPlan>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<WeightLog> WeightLogs => Set<WeightLog>();

    // Nutrition
    public DbSet<MealLog> MealLogs => Set<MealLog>();
    public DbSet<MealSchedule> MealSchedules => Set<MealSchedule>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<PushProviderConfig> PushProviderConfigs => Set<PushProviderConfig>();

    // Review
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(FitmateDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Fitmate.Domain.Common.AuditableEntity aud)
            {
                if (entry.State == EntityState.Added)
                {
                    if (aud.CreatedAt == default)
                        aud.GetType().GetProperty("CreatedAt")?.SetValue(aud, now);
                }
                if (entry.State == EntityState.Modified)
                {
                    aud.GetType().GetProperty("UpdatedAt")?.SetValue(aud, now);
                }
            }
            else if (entry.Entity is ApplicationUser appUser)
            {
                if (entry.State == EntityState.Added)
                {
                    if (appUser.CreatedAt == default) appUser.CreatedAt = now;
                    appUser.UpdatedAt = now;
                }
                if (entry.State == EntityState.Modified)
                {
                    appUser.UpdatedAt = now;
                }
            }
        }
    }
}
