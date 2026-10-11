using System.Data;
using Fitmate.Application.Ai;
using Fitmate.Domain.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitmate.Infrastructure.Ai;

public sealed class EfAiUsageService : IAiUsageService
{
    private readonly FitmateDbContext _db;
    private readonly AiUsageOptions _options;
    private readonly IAiSubscriptionService _subscriptions;

    public EfAiUsageService(FitmateDbContext db, IOptions<AiUsageOptions> options,
        IAiSubscriptionService subscriptions)
    {
        _db = db;
        _options = options.Value;
        _subscriptions = subscriptions;
    }

    public async Task<AiUsageView> GetAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var day = VietnamDay(now);
        var rows = await _db.AiUsageReservations.AsNoTracking()
            .Where(x => x.UserId == userId && x.LocalDate == day &&
                (x.State == "committed" || x.State == "reserved" && x.ExpiresAtUtc > now))
            .GroupBy(x => x.Kind)
            .Select(x => new { Kind = x.Key, Count = x.Count() })
            .ToListAsync(ct);
        var chatUsed = rows.FirstOrDefault(x => x.Kind == "chat")?.Count ?? 0;
        var scanUsed = rows.FirstOrDefault(x => x.Kind == "scan")?.Count ?? 0;
        var plan = (await _subscriptions.GetAsync(userId, ct)).Plan;
        return new AiUsageView(plan, _options.Enabled, ResetUtc(day),
            Item(plan == "plus" ? _options.PlusChatPerDay : _options.FreeChatPerDay, chatUsed),
            Item(plan == "plus" ? _options.PlusScansPerDay : _options.FreeScansPerDay, scanUsed));
    }

    public async Task<AiUsageReservation> ReserveAsync(Guid userId, string kind, CancellationToken ct)
    {
        if (!_options.Enabled) return new AiUsageReservation(Guid.Empty, false);
        if (userId == Guid.Empty || kind is not ("chat" or "scan"))
            throw new ArgumentException("Invalid AI usage reservation.");

        var plan = (await _subscriptions.GetAsync(userId, ct)).Plan;
        var limit = kind == "chat"
            ? plan == "plus" ? _options.PlusChatPerDay : _options.FreeChatPerDay
            : plan == "plus" ? _options.PlusScansPerDay : _options.FreeScansPerDay;
        if (limit <= 0) throw new AiQuotaExceededException();
        var id = Guid.NewGuid();
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            foreach (var entry in _db.ChangeTracker.Entries<AiUsageReservationRecord>()
                         .Where(x => x.Entity.Id == id))
                entry.State = EntityState.Detached;
            if (await _db.AiUsageReservations.AsNoTracking().AnyAsync(x => x.Id == id, ct))
                return new AiUsageReservation(id, true);
            var now = DateTime.UtcNow;
            var day = VietnamDay(now);
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using (var command = _db.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
                command.Parameters.Add(new NpgsqlParameter("key", $"ai:{userId:N}:{day:yyyyMMdd}:{kind}"));
                await command.ExecuteScalarAsync(ct);
            }

            var used = await _db.AiUsageReservations.CountAsync(x => x.UserId == userId &&
                x.LocalDate == day && x.Kind == kind &&
                (x.State == "committed" || x.State == "reserved" && x.ExpiresAtUtc > now), ct);
            if (used >= limit) throw new AiQuotaExceededException();

            _db.AiUsageReservations.Add(new AiUsageReservationRecord
            {
                Id = id, UserId = userId, Kind = kind, LocalDate = day,
                State = "reserved", CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(10)
            });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new AiUsageReservation(id, true);
        });
    }

    public Task CommitAsync(AiUsageReservation reservation, CancellationToken ct) =>
        ChangeStateAsync(reservation, "committed", ct);

    public Task ReleaseAsync(AiUsageReservation reservation, CancellationToken ct) =>
        ChangeStateAsync(reservation, "released", ct);

    private async Task ChangeStateAsync(AiUsageReservation reservation, string state, CancellationToken ct)
    {
        if (!reservation.Enforced) return;
        await _db.AiUsageReservations.Where(x => x.Id == reservation.Id && x.State == "reserved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, state), ct);
    }

    private static DateOnly VietnamDay(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(7));
    private static DateTime ResetUtc(DateOnly day) => day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7);
    private static AiUsageItemView Item(int limit, int used) => new(limit, used, Math.Max(0, limit - used));
}
