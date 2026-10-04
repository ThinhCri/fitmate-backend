using Fitmate.Application.Chat;
using Fitmate.Domain.Chat;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Persistence.Chat;

public sealed class EfAiChatStore : IAiChatStore
{
    private readonly FitmateDbContext _db;
    public EfAiChatStore(FitmateDbContext db) => _db = db;

    public async Task<IReadOnlyList<AiThreadSummaryRow>> ListThreadsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.ChatThreads.AsNoTracking()
            .Where(x => x.MemberId == userId && x.DeletedAt == null)
            .OrderByDescending(x => x.LastMessageAt).Take(100).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var counts = await _db.ChatMessages.AsNoTracking()
            .Where(x => x.ThreadId.HasValue && ids.Contains(x.ThreadId.Value))
            .GroupBy(x => x.ThreadId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return rows.Select(x => new AiThreadSummaryRow(x, counts.GetValueOrDefault(x.Id))).ToList();
    }

    public Task<ChatThread?> FindThreadAsync(Guid threadId, CancellationToken ct) =>
        _db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId && x.DeletedAt == null, ct);

    public async Task AddThreadAsync(ChatThread thread, CancellationToken ct) =>
        await _db.ChatThreads.AddAsync(thread, ct);

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid threadId, int limit, CancellationToken ct)
    {
        var descending = await _db.ChatMessages.AsNoTracking().Where(x => x.ThreadId == threadId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit).ToListAsync(ct);
        descending.Reverse();
        return descending;
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct) =>
        await _db.ChatMessages.AddAsync(message, ct);

    public async Task RemoveMessagesAsync(Guid threadId, CancellationToken ct)
    {
        var messages = await _db.ChatMessages.Where(x => x.ThreadId == threadId).ToListAsync(ct);
        _db.ChatMessages.RemoveRange(messages);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
