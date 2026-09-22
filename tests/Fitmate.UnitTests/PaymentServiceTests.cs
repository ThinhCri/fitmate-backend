using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class PaymentServiceTests
{
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OtherMemberId = Guid.NewGuid();
    private static readonly Guid PtId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();
    private static readonly long AmountVnd = 250_000L;
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(5);
    private static readonly DateTime SlotStartUtc = DateTime.UtcNow.AddMinutes(10);

    private sealed class InMemoryPaymentStore : IPaymentStore
    {
        public List<Payment> Payments { get; } = new();
        public BookingHold? Hold { get; set; }
        public Appointment? Appt { get; set; }

        public Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Payments.FirstOrDefault(p => p.Id == id));

        public Task<Payment?> FindByAppointmentIdAsync(Guid apptId, CancellationToken ct)
            => Task.FromResult(Payments.FirstOrDefault(p => p.AppointmentId == apptId));

        public Task<BookingHold?> FindHoldAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Hold);

        public Task<Appointment?> FindAppointmentByHoldAsync(Guid holdId, CancellationToken ct)
            => Task.FromResult(Appt);

        public void Add(Payment payment) => Payments.Add(payment);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (InMemoryPaymentStore store, Guid holdId, Guid apptId) Setup(
        Guid holdMemberId = default, bool releaseHold = false)
    {
        holdMemberId = holdMemberId == default ? MemberId : holdMemberId;
        var hold = BookingHold.Create(holdMemberId, PtId, ServiceId, SlotStartUtc, HoldTtl);
        var appt = Appointment.CreateHold(holdMemberId, PtId, ServiceId, SlotStartUtc, SlotStartUtc, 60, AmountVnd);
        if (releaseHold) hold.Release();
        return (new InMemoryPaymentStore { Hold = hold, Appt = appt }, hold.Id, appt.Id);
    }

    private static PaymentService Sut(InMemoryPaymentStore store)
        => new(store, new StubPaymentProvider());

    [Fact]
    public async Task CreateAsync_returns_payment_view_with_mock_redirect_url()
    {
        var (store, holdId, _) = Setup();

        var result = await Sut(store).CreateAsync(MemberId, new CreatePaymentRequest
        {
            HoldId = holdId,
            Method = PaymentMethod.Vnpay
        }, CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Pending);
        result.RedirectUrl.Should().StartWith("https://mock.fitmate.local/payments/");
        result.ProviderTxnRef.Should().BeNull();
        store.Payments.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateAsync_when_missing_provider_throws_ServiceUnavailable_503()
    {
        var (store, holdId, _) = Setup();
        var missingProvider = new MissingPaymentProvider("Payment provider is not configured.");
        var service = new PaymentService(store, missingProvider);

        var act = () => service.CreateAsync(MemberId, new CreatePaymentRequest
        {
            HoldId = holdId,
            Method = PaymentMethod.Vnpay
        }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.Payment.ProviderUnavailable);
        ex.Which.HttpStatus.Should().Be(503);
    }

    [Fact]
    public async Task CreateAsync_zero_or_negative_amount_throws_ValidationException()
    {
        var (store, holdId, apptId) = Setup();
        // Replace appt with zero amount
        var zeroAppt = Appointment.CreateHold(MemberId, PtId, ServiceId, SlotStartUtc, SlotStartUtc, 60, 0L);
        store.Appt = zeroAppt;

        var act = () => Sut(store).CreateAsync(MemberId, new CreatePaymentRequest
        {
            HoldId = holdId,
            Method = PaymentMethod.Vnpay
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Code == PaymentErrorCodes.InvalidAmount);
    }

    [Fact]
    public async Task CreateAsync_idempotent_returns_existing_payment_for_same_hold()
    {
        var (store, holdId, _) = Setup();
        var request = new CreatePaymentRequest { HoldId = holdId, Method = PaymentMethod.Vnpay };

        var first = await Sut(store).CreateAsync(MemberId, request, CancellationToken.None);
        var second = await Sut(store).CreateAsync(MemberId, request, CancellationToken.None);

        first.Id.Should().Be(second.Id);
        store.Payments.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateAsync_throws_when_hold_not_found()
    {
        var store = new InMemoryPaymentStore();
        var request = new CreatePaymentRequest { HoldId = Guid.NewGuid(), Method = PaymentMethod.Vnpay };

        var act = () => Sut(store).CreateAsync(MemberId, request, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>()
            .Where(e => e.Code == PaymentErrorCodes.HoldNotFound);
    }

    [Fact]
    public async Task CreateAsync_throws_NotOwner_when_hold_belongs_to_other_member()
    {
        var (store, holdId, _) = Setup(holdMemberId: OtherMemberId);
        var request = new CreatePaymentRequest { HoldId = holdId, Method = PaymentMethod.Vnpay };

        var act = () => Sut(store).CreateAsync(MemberId, request, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.Code == PaymentErrorCodes.NotOwner);
    }

    [Fact]
    public async Task CreateAsync_throws_HoldExpired_when_hold_inactive()
    {
        var (store, holdId, _) = Setup(releaseHold: true);
        var request = new CreatePaymentRequest { HoldId = holdId, Method = PaymentMethod.Vnpay };

        var act = () => Sut(store).CreateAsync(MemberId, request, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == PaymentErrorCodes.HoldExpired);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_payment_not_found()
    {
        var store = new InMemoryPaymentStore();
        var result = await Sut(store).GetByIdAsync(MemberId, Guid.NewGuid(), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public void VnpayReturn_missing_signature_returns_unverified()
    {
        var result = Sut(new InMemoryPaymentStore()).HandleVnpayReturn(
            new VnpayReturnQuery(null, "txn123", "00"));
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("unverified");
        json.Should().Contain("97");
    }

    [Fact]
    public void VnpayReturn_valid_signature_success_code_returns_paid()
    {
        var result = Sut(new InMemoryPaymentStore()).HandleVnpayReturn(
            new VnpayReturnQuery("valid_sig", "txn123", "00"));
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("paid");
    }

    [Fact]
    public void VnpayIpn_missing_signature_returns_97_checksum()
    {
        var result = Sut(new InMemoryPaymentStore()).HandleVnpayIpn(
            new VnpayReturnQuery(null, "txn123", "00"));
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("97");
    }

    [Fact]
    public void VnpayIpn_valid_signature_returns_00_success()
    {
        var result = Sut(new InMemoryPaymentStore()).HandleVnpayIpn(
            new VnpayReturnQuery("valid_sig", "txn123", "00"));
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("00");
    }
}
