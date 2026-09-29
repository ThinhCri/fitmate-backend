using Fitmate.Application.Admin;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminReviewStore(FitmateDbContext db) : IAdminReviewStore
{
    public async Task<AuditPage> AuditAsync(Guid? targetId, string? action, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (targetId.HasValue) query = query.Where(x => x.TargetId == targetId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        var total = await query.CountAsync(ct);
        // Metadata is deliberately excluded: it can contain private case/payment details.
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditItem(x.Id, x.ActorId, x.Action, x.TargetType, x.TargetId, x.Reason, x.CreatedAt)).ToListAsync(ct);
        return new(rows, total, page, pageSize);
    }
    public async Task<BookingReview?> BookingAsync(Guid id, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (appointment is null) return null;
        var payments = await db.Payments.AsNoTracking().Where(x => x.AppointmentId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.AppointmentId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return new(id, appointment.Status.ToString(),
            payments.Select(x => new BookingPaymentItem(x.Id, x.Status.ToString(), x.Method.ToString().ToLowerInvariant(), x.AmountVnd, x.RefundedSoFarVnd, x.CreatedAt)).ToList(),
            refunds.Select(x => new BookingRefundItem(x.Id, x.PaymentId, x.Status.ToString(), x.AmountVnd, x.Reason, x.ProviderRefundRef, x.CreatedAt)).ToList());
    }
    public Task<TrainerReview?> TrainerAsync(Guid id, CancellationToken ct) => db.PtProfiles.AsNoTracking()
        .Where(x => x.UserId == id).Select(x => new TrainerReview(x.Specialty, x.ExperienceYears, x.Bio)).SingleOrDefaultAsync(ct);
}
