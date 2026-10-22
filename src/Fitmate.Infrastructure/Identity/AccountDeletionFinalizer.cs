using Fitmate.Application.Storage;
using Fitmate.Application.Identity;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitmate.Infrastructure.Identity;

/// <summary>
/// Idempotent per-account erasure. The pending user row is the durable retry
/// record: it is marked Deleted only after storage and database scrub succeed.
/// A row lock serializes finalization with admin restore.
/// </summary>
public sealed class AccountDeletionFinalizer(
    FitmateDbContext db,
    IStorageService storage,
    IOwnedStorageKeyListing storageKeys,
    IOptions<StorageOptions> storageOptions)
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    public async Task<bool> FinalizeAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC time required.", nameof(nowUtc));
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM asp_net_users WHERE \"Id\"=@userId FOR UPDATE",
                [new NpgsqlParameter("userId", userId)], ct);
            var user = await db.Set<ApplicationUser>().IgnoreQueryFilters()
                .SingleOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null || user.Status == UserStatus.Deleted) return false;
            if (user.Status != UserStatus.PendingDeletion || user.DeletedAt is null ||
                user.DeletedAt.Value.Add(GracePeriod) > nowUtc) return false;

            var store = new EfAccountDeletionStore(db);
            if (await store.HasActiveBookingsAsync(userId, ct))
                throw new InvalidOperationException("Account still has unsettled booking or financial obligations.");

            // The uploader-scoped inventory includes orphaned uploads that were
            // never attached to a row. Referenced legacy keys are collected too.
            var media = new HashSet<string>(await storageKeys.ListOwnedKeysAsync(userId, ct), StringComparer.Ordinal);
            Add(media, user.AvatarStorageKey);
            foreach (var key in await db.Certificates.Where(x => x.PtUserId == userId || x.ImageUploadedByUserId == userId)
                         .Select(x => x.ImageStorageKey).ToListAsync(ct)) Add(media, key);
            foreach (var post in await db.Posts.Where(x => x.AuthorId == userId)
                         .Select(x => new { x.ImageStorageKey, x.ImageStorageKeys }).ToListAsync(ct))
            { Add(media, post.ImageStorageKey); AddRange(media, post.ImageStorageKeys); }
            foreach (var keys in await db.ChatMessages.Where(x => x.SenderUserId == userId ||
                         (x.ThreadId != null && db.ChatThreads.Any(t => t.Id == x.ThreadId && t.MemberId == userId)))
                         .Select(x => x.ImageStorageKeys).ToListAsync(ct)) AddRange(media, keys);
            foreach (var key in await db.MealLogs.Where(x => x.UserId == userId)
                         .Select(x => x.ImageStorageKey).ToListAsync(ct)) Add(media, key);

            // Storage deletion is idempotent. If it fails, the DB transaction
            // rolls back and the pending row causes a retry on the next sweep.
            foreach (var key in media)
            {
                if (!IsOwnedStorageKey(key, userId, storageOptions.Value.AllowedCategories))
                    throw new InvalidOperationException("An attached media key cannot be proven to belong to the account.");
                await storage.DeleteAsync(key, ct);
            }

            await ScrubDatabaseAsync(userId, user.Email, user.PhoneNumber, ct);
            user.FullName = "[deleted]";
            user.Email = null;
            user.NormalizedEmail = null;
            user.EmailConfirmed = false;
            user.UserName = $"deleted-{userId:N}";
            user.NormalizedUserName = user.UserName.ToUpperInvariant();
            user.PhoneNumber = null;
            user.PhoneNumberConfirmed = false;
            user.LastActiveAt = null;
            user.PasswordHash = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.TwoFactorEnabled = false;
            user.AvatarUrl = null;
            user.AvatarStorageKey = null;
            user.Timezone = "UTC";
            user.DeletionRequestedAt = null;
            user.Status = UserStatus.Deleted;
            user.UpdatedAt = nowUtc;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    private static void Add(HashSet<string> keys, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
    }

    private static void AddRange(HashSet<string> keys, IEnumerable<string>? values)
    {
        if (values is null) return;
        foreach (var value in values) Add(keys, value);
    }

    private static bool IsOwnedStorageKey(string key, Guid userId, IReadOnlyList<string> categories)
    {
        if (key.Contains('\\') || key.Contains('?') || key.Contains('#'))
            return false;
        var parts = key.Split('/');
        return parts.Length == 2 &&
            categories.Contains(parts[0], StringComparer.OrdinalIgnoreCase) &&
            parts[1].StartsWith(userId.ToString("N") + "_", StringComparison.OrdinalIgnoreCase) &&
            parts[1] != "." && parts[1] != "..";
    }

    private async Task ScrubDatabaseAsync(Guid userId, string? email, string? phone, CancellationToken ct)
    {
        // All statements are parameterized and execute in the row-locked transaction.
        // Shared content is redacted rather than deleting another user's conversation.
        string[] statements =
        [
            "DELETE FROM \"AspNetUserClaims\" WHERE \"UserId\"=@userId",
            "DELETE FROM \"AspNetUserLogins\" WHERE \"UserId\"=@userId",
            "DELETE FROM \"AspNetUserTokens\" WHERE \"UserId\"=@userId",
            "DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\"=@userId",
            "DELETE FROM admin_login_challenges WHERE \"UserId\"=@userId",
            "DELETE FROM auth_sessions WHERE \"UserId\"=@userId",
            "DELETE FROM refresh_tokens WHERE \"UserId\"=@userId",
            "DELETE FROM push_deliveries WHERE device_id IN (SELECT id FROM devices WHERE user_id=@userId) OR notification_id IN (SELECT id FROM notifications WHERE user_id=@userId)",
            "DELETE FROM devices WHERE user_id=@userId",
            "DELETE FROM notifications WHERE user_id=@userId",
            "DELETE FROM user_profiles WHERE \"UserId\"=@userId",
            "DELETE FROM user_preferences WHERE user_id=@userId",
            "DELETE FROM ai_usage_reservations WHERE \"UserId\"=@userId",
            "UPDATE ai_subscriptions SET \"ProtectedPurchaseToken\"='' WHERE \"UserId\"=@userId",
            "DELETE FROM weekly_meal_plan_consumptions WHERE \"UserId\"=@userId OR \"MealLogId\" IN (SELECT id FROM meal_logs WHERE \"UserId\"=@userId) OR \"PlanItemId\" IN (SELECT \"Id\" FROM weekly_meal_plan_items WHERE \"UserId\"=@userId)",
            "DELETE FROM meal_logs WHERE \"UserId\"=@userId",
            "DELETE FROM weekly_meal_plan_items WHERE \"UserId\"=@userId",
            "DELETE FROM meal_schedules WHERE \"UserId\"=@userId",
            "DELETE FROM workout_session_exercise_sets WHERE \"WorkoutSessionExerciseId\" IN (SELECT id FROM workout_session_exercises WHERE \"WorkoutSessionId\" IN (SELECT id FROM workout_sessions WHERE \"UserId\"=@userId))",
            "DELETE FROM workout_session_exercises WHERE \"WorkoutSessionId\" IN (SELECT id FROM workout_sessions WHERE \"UserId\"=@userId)",
            "DELETE FROM workout_sessions WHERE \"UserId\"=@userId",
            "DELETE FROM weight_logs WHERE \"UserId\"=@userId",
            "DELETE FROM exercises WHERE \"WorkoutPlanId\" IN (SELECT id FROM workout_plans WHERE \"UserId\"=@userId)",
            "DELETE FROM workout_plans WHERE \"UserId\"=@userId",
            "DELETE FROM chat_message_reactions WHERE \"UserId\"=@userId OR \"MessageId\" IN (SELECT m.id FROM chat_messages m JOIN chat_threads t ON t.id=m.\"ThreadId\" WHERE t.\"MemberId\"=@userId)",
            "DELETE FROM chat_messages WHERE \"ThreadId\" IN (SELECT id FROM chat_threads WHERE \"MemberId\"=@userId)",
            "DELETE FROM chat_threads WHERE \"MemberId\"=@userId",
            "UPDATE chat_messages SET \"Text\"='[deleted]', \"ImageStorageKeys\"='{{}}'::text[] WHERE \"SenderUserId\"=@userId",
            "DELETE FROM chat_thread_reads WHERE user_id=@userId",
            "DELETE FROM chat_message_reactions WHERE \"UserId\"=@userId",
            "DELETE FROM friend_requests WHERE sender_id=@userId OR recipient_id=@userId",
            "DELETE FROM friendships WHERE user_low_id=@userId OR user_high_id=@userId",
            "DELETE FROM blocked_users WHERE blocker_id=@userId OR blocked_id=@userId",
            "DELETE FROM reactions WHERE \"UserId\"=@userId",
            "DELETE FROM post_bookmarks WHERE \"UserId\"=@userId",
            "DELETE FROM comment_likes WHERE \"UserId\"=@userId",
            "UPDATE comments SET \"Text\"='[deleted]' WHERE \"AuthorId\"=@userId",
            "UPDATE posts SET \"Content\"='[deleted]', \"ImageStorageKey\"=NULL, \"ImageStorageKeys\"='{{}}'::text[], \"Link\"=NULL, \"Tags\"='{{}}'::text[] WHERE \"AuthorId\"=@userId",
            "UPDATE reviews SET \"Text\"='[deleted]', \"ModerationReason\"=NULL WHERE \"FromUserId\"=@userId OR \"PtUserId\"=@userId",
            "UPDATE reports SET \"Reason\"='[deleted]', \"Details\"=NULL, \"AdminNote\"=NULL WHERE \"ReporterId\"=@userId OR \"TargetId\"=@userId",
            "UPDATE reports SET \"Reason\"='[deleted]', \"Details\"=NULL, \"AdminNote\"=NULL WHERE \"TargetId\" IN (SELECT id FROM posts WHERE \"AuthorId\"=@userId UNION SELECT id FROM comments WHERE \"AuthorId\"=@userId UNION SELECT id FROM certificates WHERE \"PtUserId\"=@userId)",
            "UPDATE user_moderation_actions SET \"Reason\"=NULL, \"MetadataJson\"=NULL WHERE \"UserId\"=@userId OR \"ActorAdminId\"=@userId",
            "DELETE FROM support_ticket_messages WHERE ticket_id IN (SELECT id FROM support_tickets WHERE user_id=@userId)",
            "DELETE FROM support_tickets WHERE user_id=@userId",
            "UPDATE support_ticket_messages SET content='[deleted]', attachment_urls='{{}}'::text[] WHERE ticket_id IN (SELECT id FROM support_tickets WHERE \"BookingId\" IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId))",
            "UPDATE support_tickets SET subject='[deleted]', booking_resolution_reason=NULL, booking_evidence_reference=NULL WHERE \"BookingId\" IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId)",
            "DELETE FROM certificates WHERE \"PtUserId\"=@userId OR \"ImageUploadedByUserId\"=@userId",
            "DELETE FROM pt_payout_accounts WHERE \"PtUserId\"=@userId",
            "DELETE FROM availability_slots WHERE \"PtUserId\"=@userId",
            "DELETE FROM pt_schedule_locks WHERE pt_user_id=@userId",
            "DELETE FROM services WHERE \"PtUserId\"=@userId",
            "DELETE FROM pt_profiles WHERE \"UserId\"=@userId",
            "UPDATE booking_reschedule_requests SET reason='[deleted]', rejection_reason=NULL WHERE requested_by_user_id=@userId OR appointment_id IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId)",
            "UPDATE session_package_stop_requests SET \"Reason\"='[deleted]' WHERE \"PackageId\" IN (SELECT \"Id\" FROM session_packages WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId)",
            "UPDATE appointments SET \"Note\"=NULL WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId",
            "UPDATE manual_pt_settlements SET \"SessionEvidenceReference\"=NULL, \"PayeeEvidenceReference\"=NULL, \"HoldReason\"=NULL, \"TransferEvidenceReference\"=NULL, \"PayeeBankCode\"=NULL, \"PayeeBankName\"=NULL, \"PayeeAccountNumber\"=NULL, \"PayeeAccountName\"=NULL WHERE \"PtUserId\"=@userId",
            "UPDATE payments SET \"RedirectUrl\"=NULL WHERE \"AppointmentId\" IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId) OR \"PackageId\" IN (SELECT \"Id\" FROM session_packages WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId)",
            "UPDATE refunds SET \"Reason\"='[deleted]' WHERE \"AppointmentId\" IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId)",
            "UPDATE financial_ledger_entries SET metadata_json='{{}}' WHERE created_by_user_id=@userId::text OR appointment_id IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId) OR payment_id IN (SELECT id FROM payments WHERE \"AppointmentId\" IN (SELECT id FROM appointments WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId) OR \"PackageId\" IN (SELECT \"Id\" FROM session_packages WHERE \"MemberId\"=@userId OR \"PtUserId\"=@userId))",
            "DELETE FROM booking_holds WHERE (\"MemberId\"=@userId OR \"PtUserId\"=@userId) AND NOT EXISTS (SELECT 1 FROM session_package_sessions s WHERE s.\"HoldId\"=booking_holds.id)",
            "UPDATE audit_logs SET \"ActorId\"=NULL, \"Reason\"=NULL, \"MetadataJson\"=NULL WHERE \"ActorId\"=@userId",
            "UPDATE audit_logs SET \"TargetId\"=NULL, \"Reason\"=NULL, \"MetadataJson\"=NULL WHERE \"TargetId\"=@userId",
            "DELETE FROM idempotency_records WHERE \"ActorUserId\"=@userId::text AND NOT \"IsFinancial\"",
            "UPDATE idempotency_records SET \"ResponseBody\"=NULL WHERE \"ActorUserId\"=@userId::text",
        ];

        foreach (var sql in statements)
            await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("userId", userId)], ct);

        // Payloads from historic async tasks may contain the user's contact
        // data. Keep status/type/timing for diagnostics, never the body.
        const string scrubOutbox = "UPDATE outbox_messages SET \"PayloadJson\"='{{}}', \"LastError\"=NULL, \"Status\"=CASE WHEN \"Status\"='pending' THEN 'dead_letter' ELSE \"Status\" END WHERE \"PayloadJson\"::text ILIKE @idPattern OR \"PayloadJson\"::text ILIKE @compactIdPattern OR (@emailPattern <> '' AND \"PayloadJson\"::text ILIKE @emailPattern) OR (@phonePattern <> '' AND \"PayloadJson\"::text LIKE @phonePattern)";
        await db.Database.ExecuteSqlRawAsync(scrubOutbox,
            [new NpgsqlParameter("idPattern", "%" + userId + "%"),
             new NpgsqlParameter("compactIdPattern", "%" + userId.ToString("N") + "%"),
             new NpgsqlParameter("emailPattern", string.IsNullOrWhiteSpace(email) ? "" : "%" + email + "%"),
             new NpgsqlParameter("phonePattern", string.IsNullOrWhiteSpace(phone) ? "" : "%" + phone + "%")], ct);

        const string scrubReferences = """
            UPDATE notifications SET title='[deleted]', message='[deleted]', summary=NULL, payload_json=NULL
            WHERE payload_json::text ILIKE @idPattern OR payload_json::text ILIKE @compactIdPattern
               OR (@emailPattern <> '' AND payload_json::text ILIKE @emailPattern)
               OR (@phonePattern <> '' AND payload_json::text LIKE @phonePattern);
            UPDATE idempotency_records SET "ResponseBody"=NULL
            WHERE "ResponseBody"::text ILIKE @idPattern OR "ResponseBody"::text ILIKE @compactIdPattern
               OR (@emailPattern <> '' AND "ResponseBody"::text ILIKE @emailPattern)
               OR (@phonePattern <> '' AND "ResponseBody"::text LIKE @phonePattern);
            UPDATE audit_logs SET "Reason"=NULL, "MetadataJson"=NULL
            WHERE "MetadataJson"::text ILIKE @idPattern OR "MetadataJson"::text ILIKE @compactIdPattern
               OR (@emailPattern <> '' AND "MetadataJson"::text ILIKE @emailPattern)
               OR (@phonePattern <> '' AND "MetadataJson"::text LIKE @phonePattern);
            UPDATE financial_ledger_entries SET metadata_json='{{}}'
            WHERE metadata_json::text ILIKE @idPattern OR metadata_json::text ILIKE @compactIdPattern
               OR (@emailPattern <> '' AND metadata_json::text ILIKE @emailPattern)
               OR (@phonePattern <> '' AND metadata_json::text LIKE @phonePattern);
            """;
        await db.Database.ExecuteSqlRawAsync(scrubReferences,
            [new NpgsqlParameter("idPattern", "%" + userId + "%"),
             new NpgsqlParameter("compactIdPattern", "%" + userId.ToString("N") + "%"),
             new NpgsqlParameter("emailPattern", string.IsNullOrWhiteSpace(email) ? "" : "%" + email + "%"),
             new NpgsqlParameter("phonePattern", string.IsNullOrWhiteSpace(phone) ? "" : "%" + phone + "%")], ct);
    }
}

public sealed class AccountDeletionFinalizationWorker(IServiceScopeFactory scopeFactory,
    AccountDeletionFinalizationGate gate, ILogger<AccountDeletionFinalizationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            if (!gate.Ready) continue;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-30);
                var due = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Status == UserStatus.PendingDeletion && u.DeletedAt <= cutoff)
                    .OrderBy(u => u.DeletedAt).Select(u => u.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var id in due)
                {
                    try
                    {
                        using var itemScope = scopeFactory.CreateScope();
                        await itemScope.ServiceProvider.GetRequiredService<AccountDeletionFinalizer>()
                            .FinalizeAsync(id, DateTime.UtcNow, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Account deletion finalization failed for {UserId}; will retry", id);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Account deletion sweep failed; will retry");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
