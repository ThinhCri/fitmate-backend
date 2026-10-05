using Fitmate.Application.Chat;
using Fitmate.Domain.Chat;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Persistence.Chat;

/// <summary>
/// BE-191: EF Core store cho FriendConversation + ChatThreadRead + ChatMessage (friend scope).
/// Mọi write đều tracked trong DbContext hiện tại; SaveChangesAsync ở Application commit
/// cùng outbox message (transaction semantics từ SaveChanges).
/// </summary>
public class EfChatStore : IChatStore
{
    private readonly FitmateDbContext _db;

    public EfChatStore(FitmateDbContext db) { _db = db; }

    public async Task<FriendConversation> GetOrCreateConversationAsync(Guid a, Guid b, CancellationToken ct)
    {
        var (low, high) = FriendConversation.CanonicalPair(a, b);

        var existing = await _db.FriendConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserLowId == low && c.UserHighId == high, ct);
        if (existing is not null) return existing;

        var fresh = FriendConversation.Create(a, b);
        await _db.FriendConversations.AddAsync(fresh, ct);

        // Race-safe: cùng SaveChangesAsync với caller (transaction). UNIQUE constraint
        // bảo vệ khi 2 request đồng thời: insert thứ 2 sẽ nhận DbUpdateException, caller
        // retry bằng cách FindConversationForPairAsync.
        try
        {
            await _db.SaveChangesAsync(ct);
            return fresh;
        }
        catch (DbUpdateException)
        {
            // Detach conflicting tracked entity
            _db.Entry(fresh).State = EntityState.Detached;
            var resolved = await FindConversationForPairAsync(a, b, ct);
            if (resolved is null) throw;
            return resolved;
        }
    }

    public Task<FriendConversation?> FindConversationAsync(Guid conversationId, CancellationToken ct)
        => _db.FriendConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);

    public async Task<FriendConversation?> FindConversationForPairAsync(Guid a, Guid b, CancellationToken ct)
    {
        var (low, high) = FriendConversation.CanonicalPair(a, b);
        return await _db.FriendConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserLowId == low && c.UserHighId == high, ct);
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct)
    {
        await _db.ChatMessages.AddAsync(message, ct);
    }

    public Task<ChatMessage?> FindMessageByClientMessageIdAsync(Guid conversationId, Guid clientMessageId, CancellationToken ct)
        => _db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.ClientMessageId == clientMessageId, ct);

    public Task<ChatMessage?> GetLastMessageAsync(Guid conversationId, CancellationToken ct)
        => _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ConversationSummaryRow>> ListConversationsAsync(
        Guid userId, DateTime? cursorLastMessageAt, Guid? cursorConversationId, int limit, CancellationToken ct)
    {
        // 1) conversations user là participant
        var q = _db.FriendConversations.AsNoTracking()
            .Where(c => c.UserLowId == userId || c.UserHighId == userId);

        // 2) cursor
        if (cursorLastMessageAt.HasValue && cursorConversationId.HasValue)
        {
            var ca = cursorLastMessageAt.Value;
            var cid = cursorConversationId.Value;
            q = q.Where(c => c.LastMessageAt < ca
                          || (c.LastMessageAt == ca && c.Id.CompareTo(cid) < 0));
        }

        var conversations = await q
            .OrderByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);

        if (conversations.Count == 0) return Array.Empty<ConversationSummaryRow>();

        var convIds = conversations.Select(c => c.Id).ToList();

        // 3) last messages per conversation (1 query, group)
        var lastMessages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId != null && convIds.Contains(m.ConversationId.Value))
            .GroupBy(m => m.ConversationId!.Value)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).First())
            .ToListAsync(ct);

        var lastByConv = lastMessages.ToDictionary(m => m.ConversationId!.Value);

        // 4) unread count: cần last_read_at cho viewer
        var reads = await _db.ChatThreadReads.AsNoTracking()
            .Where(r => r.UserId == userId && convIds.Contains(r.ConversationId))
            .ToListAsync(ct);
        var readByConv = reads.ToDictionary(r => r.ConversationId);

        // 5) unread messages: mỗi conversation đếm msg do peer gửi sau marker
        var readMap = readByConv.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.LastReadAt ?? DateTime.MinValue);

        var unreadQuery = from m in _db.ChatMessages.AsNoTracking()
                          where m.ConversationId != null
                             && convIds.Contains(m.ConversationId.Value)
                             && m.SenderUserId != userId
                          group m by m.ConversationId!.Value into g
                          select new { ConversationId = g.Key, Count = g.Count() };

        // For monotonic ordering, last_read_at filter được áp dụng in-memory
        // sau khi đã gom theo conversationId (số lượng conv nhỏ, OK).
        var lastMsgIds = readByConv.Values
            .Where(r => r.LastReadMessageId.HasValue)
            .Select(r => r.LastReadMessageId!.Value)
            .ToList();

        var lastReadMsgMap = lastMsgIds.Count == 0
            ? new Dictionary<Guid, DateTime>()
            : await _db.ChatMessages.AsNoTracking()
                .Where(m => lastMsgIds.Contains(m.Id))
                .Select(m => new { m.Id, m.CreatedAt })
                .ToDictionaryAsync(x => x.Id, x => x.CreatedAt, ct);

        var rows = new List<ConversationSummaryRow>();
        foreach (var conv in conversations)
        {
            lastByConv.TryGetValue(conv.Id, out var last);
            readByConv.TryGetValue(conv.Id, out var readRow);

            int unread;
            if (readRow is null)
            {
                unread = await _db.ChatMessages.AsNoTracking()
                    .CountAsync(m => m.ConversationId == conv.Id && m.SenderUserId != userId, ct);
            }
            else if (readRow.LastReadMessageId.HasValue && lastReadMsgMap.TryGetValue(readRow.LastReadMessageId.Value, out var readAt))
            {
                unread = await _db.ChatMessages.AsNoTracking()
                    .CountAsync(m => m.ConversationId == conv.Id
                                  && m.SenderUserId != userId
                                  && m.CreatedAt > readAt, ct);
            }
            else
            {
                unread = 0;
            }

            var peer = conv.UserLowId == userId ? conv.UserHighId : conv.UserLowId;
            rows.Add(new ConversationSummaryRow(conv.Id, peer, conv.LastMessageAt, last, unread));
        }
        return rows;
    }

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
        Guid conversationId, DateTime? cursorCreatedAt, Guid? cursorMessageId, int limit, CancellationToken ct)
    {
        var q = _db.ChatMessages.AsNoTracking().Include(m => m.Reactions)
            .Where(m => m.ConversationId == conversationId);

        if (cursorCreatedAt.HasValue && cursorMessageId.HasValue)
        {
            var ca = cursorCreatedAt.Value;
            var mid = cursorMessageId.Value;
            q = q.Where(m => m.CreatedAt < ca
                          || (m.CreatedAt == ca && m.Id.CompareTo(mid) < 0));
        }

        return await q
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<ChatMessage?> FindMessageAsync(Guid messageId, CancellationToken ct)
        => _db.ChatMessages.Include(x => x.Reactions)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<ChatMessageReaction?> FindReactionAsync(Guid messageId, Guid userId, CancellationToken ct)
        => _db.ChatMessageReactions.FirstOrDefaultAsync(x => x.MessageId == messageId && x.UserId == userId, ct);

    public Task AddReactionAsync(ChatMessageReaction reaction, CancellationToken ct)
        => _db.ChatMessageReactions.AddAsync(reaction, ct).AsTask();

    public void RemoveReaction(ChatMessageReaction reaction) => _db.ChatMessageReactions.Remove(reaction);

    public Task<ChatThreadRead?> GetThreadReadAsync(Guid conversationId, Guid userId, CancellationToken ct)
        => _db.ChatThreadReads.FirstOrDefaultAsync(r => r.ConversationId == conversationId && r.UserId == userId, ct);

    public async Task UpsertThreadReadAsync(ChatThreadRead read, CancellationToken ct)
    {
        // Nếu đã tracked → update; nếu chưa → add.
        var existing = await _db.ChatThreadReads.FirstOrDefaultAsync(
            r => r.ConversationId == read.ConversationId && r.UserId == read.UserId, ct);
        if (existing is null)
        {
            await _db.ChatThreadReads.AddAsync(read, ct);
        }
        else
        {
            // Cập nhật in-place thông qua reflection (private setters)
            existing.GetType().GetProperty("LastReadMessageId")?.SetValue(existing, read.LastReadMessageId);
            existing.GetType().GetProperty("LastReadAt")?.SetValue(existing, read.LastReadAt);
            existing.GetType().GetProperty("UpdatedAt")?.SetValue(existing, DateTime.UtcNow);
        }
    }

    public async Task<int> CountUnreadAsync(Guid conversationId, Guid viewerId, DateTime? lastReadAt, CancellationToken ct)
    {
        var q = _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.SenderUserId != viewerId);
        if (lastReadAt.HasValue)
            q = q.Where(m => m.CreatedAt > lastReadAt.Value);
        return await q.CountAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
