using System.Text.Json;
using Fitmate.Application.Outbox;
using Fitmate.Application.Finance;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Payments.Payos;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.IntegrationTests.Payments;

// No real provider calls. Uses the isolated PostgreSQL database from the existing fixture.
public class PayosPostgresIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;
    private readonly PayosOptions _options = new()
    {
        Enabled = true, MerchantApproved = true, ClientId = "test-client", ApiKey = "test-key",
        ChecksumKey = "test-checksum-not-a-live-secret", AccountNumber = "00000000",
        ReturnUrl = "https://example.test/return", CancelUrl = "https://example.test/cancel"
    };
    public PayosPostgresIntegrationTests(FitmateTestAppFactory factory)
    { _factory = factory; factory.EnsureDatabaseInitialized(); }

    private async Task<(Guid PaymentId, long OrderCode)> SeedAsync(bool expired = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var member = Guid.NewGuid(); var pt = Guid.NewGuid(); var service = Guid.NewGuid(); var time = DateTime.UtcNow.AddHours(2);
        var hold = BookingHold.Create(member, pt, service, time, TimeSpan.FromMinutes(5));
        if (expired) hold.Release();
        var appt = Appointment.CreateHold(member, pt, service, time, time, 60, 100000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 100000, null);
        var order = PayosOrder.Create(payment.Id, hold.ExpiresAt);
        db.AddRange(hold, appt, payment, order);
        await db.SaveChangesAsync();
        return (payment.Id, order.OrderCode);
    }

    private JsonElement Webhook(long code, long amount = 100000, string account = "00000000", bool tamper = false)
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["orderCode"] = code, ["amount"] = amount, ["accountNumber"] = account,
            ["currency"] = "VND", ["code"] = "00", ["reference"] = "test-ref-" + code,
            ["paymentLinkId"] = "123456781234123412341234567890ab"
        };
        var canonical = string.Join("&", values.Select(p => p.Key + "=" + p.Value));
        var signature = PayosSignature.Sign(canonical, _options.ChecksumKey);
        if (tamper) values["amount"] = amount + 1;
        return JsonSerializer.SerializeToElement(new { data = values, signature, success = true, code = "00" });
    }

    private async Task DeliverAsync(JsonElement body)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = new PayosWebhookHandler(scope.ServiceProvider.GetRequiredService<FitmateDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPaymentStore>(), scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>(), Options.Create(_options));
        await handler.HandleAsync(body, default);
    }

    [Fact]
    public async Task Concurrent_duplicates_settle_once_without_a_payout()
    {
        var seed = await SeedAsync();
        var body = Webhook(seed.OrderCode);
        await Task.WhenAll(DeliverAsync(body), DeliverAsync(body));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        payment.Status.Should().Be(PaymentStatus.Paid);
        var appt = await db.Appointments.SingleAsync(a => a.Id == payment.AppointmentId);
        appt.Status.Should().Be(AppointmentStatus.Pending);
        appt.TrainerShareVnd.Should().Be(90000);
        (await db.Set<ManualPtSettlement>().AnyAsync(s => s.AppointmentId == appt.Id)).Should().BeFalse();
        (await db.Refunds.CountAsync(r => r.PaymentId == payment.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Late_payment_creates_one_pending_manual_refund_without_restoring_booking()
    {
        var seed = await SeedAsync(expired: true);
        await DeliverAsync(Webhook(seed.OrderCode));
        await DeliverAsync(Webhook(seed.OrderCode));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        payment.Status.Should().Be(PaymentStatus.Paid);
        (await db.Refunds.SingleAsync(r => r.PaymentId == payment.Id)).Status.Should().Be(RefundStatus.Pending);
        (await db.Appointments.SingleAsync(a => a.Id == payment.AppointmentId)).PaymentId.Should().BeNull();
    }

    [Theory]
    [InlineData(100001, "00000000", false)]
    [InlineData(100000, "99999999", false)]
    [InlineData(100000, "00000000", true)]
    public async Task Invalid_amount_account_or_signature_never_changes_payment(long amount, string account, bool tamper)
    {
        var seed = await SeedAsync();
        var act = () => DeliverAsync(Webhook(seed.OrderCode, amount, account, tamper));
        await act.Should().ThrowAsync<DomainException>();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Pending);
    }

    private async Task<(Guid AppointmentId, Guid PaymentId)> SeedCompletedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var time = DateTime.UtcNow.AddDays(-1);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), time, time, 60, 100000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 100000, null);
        payment.MarkPaid("test-completed-" + Guid.NewGuid());
        appt.AttachPayment(payment.Id, 100000, 0, .1m, 10000, 90000, "v1");
        appt.CompleteByPt();
        db.AddRange(appt, payment);
        await db.SaveChangesAsync();
        return (appt.Id, payment.Id);
    }

    private async Task ApproveAsync(Guid id, Guid admin)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        await store.UpdateAsync(id, admin, "finance.settlement_approved", c => c.Settlement.Approve(admin,
            ManualSettlementService.EligibleAmount(c), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-4),
            c.Appointment.SlotStartUtc.AddMinutes(c.Appointment.DurationMinutes), "test:session", "test:payee", true, DateTime.UtcNow), default);
    }

    [Fact]
    public async Task Concurrent_manual_receipts_record_one_payout_and_preserve_approval()
    {
        var seed = await SeedCompletedAsync();
        var admin = Guid.NewGuid();
        await ApproveAsync(seed.AppointmentId, admin);
        var time = DateTime.UtcNow;
        var reference = "test-bank-" + Guid.NewGuid();
        async Task RecordAsync()
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IManualSettlementStore>().UpdateAsync(seed.AppointmentId, admin,
                "finance.trainer_settled", c => c.Settlement.RecordTransfer(admin, 90000, reference, "test:receipt", time, DateTime.UtcNow), default);
        }
        await Task.WhenAll(RecordAsync(), RecordAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var record = await db.Set<ManualPtSettlement>().SingleAsync(s => s.AppointmentId == seed.AppointmentId);
        record.Status.Should().Be("paid");
        record.AmountVnd.Should().Be(90000);
        record.CustomerConfirmedAt.Should().NotBeNull();
        (await db.AuditLogs.CountAsync(a => a.TargetId == seed.AppointmentId && a.Action == "finance.trainer_settled")).Should().Be(1);
    }

    [Fact]
    public async Task Refund_intent_holds_approved_payout_and_only_receipt_changes_money()
    {
        var seed = await SeedCompletedAsync();
        var admin = Guid.NewGuid();
        await ApproveAsync(seed.AppointmentId, admin);
        Guid refundId;
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            var refund = await store.CreateRefundAsync(admin, new(seed.PaymentId, 10000, "test refund"), default);
            refund.Status.Should().Be(RefundStatus.Pending);
            refundId = refund.Id;
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(0);
            (await db.Set<ManualPtSettlement>().SingleAsync(s => s.AppointmentId == seed.AppointmentId)).Status.Should().Be("on_hold");
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            var act = () => store.UpdateAsync(seed.AppointmentId, admin, "finance.trainer_settled", c =>
                c.Settlement.RecordTransfer(admin, ManualSettlementService.EligibleAmount(c), "test-bank", "test:receipt", DateTime.UtcNow, DateTime.UtcNow), default);
            await act.Should().ThrowAsync<ConflictException>();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            var request = new ConfirmManualRefundRequest("test-refund-" + Guid.NewGuid(), "test:receipt", DateTime.UtcNow);
            await store.ConfirmRefundAsync(admin, refundId, request, default);
            await store.ConfirmRefundAsync(admin, refundId, request, default);
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(10000);
        }
    }

    [Fact]
    public async Task Checkout_retry_uses_reserved_order_and_recovers_existing_link_without_scaling_vnd()
    {
        var seed = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var posts = 0; var gets = 0;
        var handler = new FakeHttp(async request =>
        {
            request.RequestUri!.Host.Should().Be("api-merchant.payos.vn");
            request.Headers.GetValues("x-client-id").Single().Should().Be(_options.ClientId);
            request.Headers.GetValues("x-api-key").Single().Should().Be(_options.ApiKey);
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                body.RootElement.GetProperty("orderCode").GetInt64().Should().Be(seed.OrderCode);
                body.RootElement.GetProperty("amount").GetInt64().Should().Be(100000);
                var canonical = $"amount=100000&cancelUrl={_options.CancelUrl}&description=FITMATE&orderCode={seed.OrderCode}&returnUrl={_options.ReturnUrl}";
                body.RootElement.GetProperty("signature").GetString().Should().Be(PayosSignature.Sign(canonical, _options.ChecksumKey));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"code\":\"231\"}") };
            }
            gets++;
            request.RequestUri.AbsolutePath.Should().EndWith("/" + seed.OrderCode);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { code = "00", data = new {
                    id = "123456781234123412341234567890ab", orderCode = seed.OrderCode, amount = 100000, status = "PENDING" } }))
            };
        });
        using var client = new HttpClient(handler);
        var adapter = new PayosPaymentAdapter(client, db, Options.Create(_options));
        var result = await adapter.CreatePaymentAsync(new(seed.PaymentId, 100000, "FITMATE"), default);
        await db.SaveChangesAsync();
        result.RedirectUrl.Should().Be("https://pay.payos.vn/web/123456781234123412341234567890ab");
        posts.Should().Be(1); gets.Should().Be(1);
        (await db.Set<PayosOrder>().CountAsync(o => o.PaymentId == seed.PaymentId)).Should().Be(1);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
