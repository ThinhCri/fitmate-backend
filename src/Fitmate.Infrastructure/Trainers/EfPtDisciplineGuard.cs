using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Trainers;

public sealed class EfPtDisciplineGuard(FitmateDbContext db) : IPtDisciplineGuard
{
    public async Task EnsureMayPublishAsync(Guid ptUserId, CancellationToken ct)
    {
        var profile = await db.PtProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == ptUserId, ct);
        if (profile is null || !profile.CanAcceptNewBookings(DateTime.UtcNow))
            throw new ConflictException("PT_PUBLICATION_SUSPENDED",
                "PT account cannot publish services or availability while suspended or banned.");
    }
}
