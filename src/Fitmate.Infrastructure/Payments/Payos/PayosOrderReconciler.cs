using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Payos;

public sealed class PayosOrderReconciler(
    FitmateDbContext db,
    IPaymentStore store,
    IAppointmentStore appointments,
    IOutboxDispatcher outbox,
    IOptions<PayosOptions> options,
    PayosPaymentAdapter adapter,
    ILogger<PayosOrderReconciler> logger) : IPayosOrderReconciler
{
    public async Task<ProviderCreatePaymentResult?> GetStoredPaymentDetailsAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => new { p.RedirectUrl })
            .SingleOrDefaultAsync(ct);
        var order = await db.Set<PayosOrder>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.PaymentId == paymentId, ct);
        if (order is null || string.IsNullOrWhiteSpace(payment?.RedirectUrl))
            return null;

        return new ProviderCreatePaymentResult(
            order.OrderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payment.RedirectUrl,
            order.QrCode,
            order.AccountNumber,
            order.AccountName,
            order.Bin,
            order.Description,
            order.OrderCode);
    }

    public async Task ReconcileIfPaidAsync(Guid paymentId, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled || !config.IsValid())
            return;

        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null || payment.Method != PaymentMethod.Payos || payment.Status != PaymentStatus.Pending)
            return;

        var order = await db.Set<PayosOrder>().SingleOrDefaultAsync(o => o.PaymentId == paymentId, ct);
        if (order is null)
            return;

        try
        {
            var status = await adapter.TryQueryPaidOrderAsync(order, payment.AmountVnd, ct);
            if (status is not { IsPaid: true })
                return;

            var executor = new PayosSettlementExecutor(db, store, appointments, outbox);
            await executor.SettleVerifiedOrderAsync(
                order,
                status.PaymentLinkId,
                payment.AmountVnd,
                status.Reference,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PayOS polling reconciliation failed for PaymentId {PaymentId}", paymentId);
        }
    }

    public Task<bool> IsPackagePaymentOwnedByMemberAsync(Guid packageId, Guid memberId, CancellationToken ct) =>
        db.SessionPackages.AsNoTracking().AnyAsync(p => p.Id == packageId && p.MemberId == memberId, ct);
}
