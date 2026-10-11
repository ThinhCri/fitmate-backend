using Fitmate.Application.Finance;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

public sealed class EfPtPayoutAccountStore(FitmateDbContext db) : IPtPayoutAccountStore
{
    public Task<PtPayoutAccount?> FindAsync(Guid ptUserId, CancellationToken ct) =>
        db.PtPayoutAccounts.SingleOrDefaultAsync(x => x.PtUserId == ptUserId, ct);

    public Task<bool> HasVerifiedPhoneAsync(Guid ptUserId, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.Id == ptUserId && x.PhoneNumberConfirmed && x.PhoneNumber != null, ct);

    public async Task SaveAsync(PtPayoutAccount account, CancellationToken ct)
    {
        if (db.Entry(account).State == EntityState.Detached) db.PtPayoutAccounts.Add(account);
        await db.SaveChangesAsync(ct);
    }
}
