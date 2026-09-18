using Fitmate.Application.Outbox;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;

namespace Fitmate.Infrastructure.Outbox;

/// <summary>
/// Adapter chuyển <see cref="IOutboxQueue"/> → <c>FitmateDbContext.OutboxMessages</c>.
/// </summary>
public class EfOutboxQueue : IOutboxQueue
{
    private readonly FitmateDbContext _db;

    public EfOutboxQueue(FitmateDbContext db)
    {
        _db = db;
    }

    public void Add(OutboxMessage message)
    {
        _db.OutboxMessages.Add(message);
    }
}
