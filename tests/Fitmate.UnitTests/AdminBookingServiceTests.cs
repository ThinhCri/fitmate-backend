using Fitmate.Application.Auth;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public class AdminBookingServiceTests
{
    private sealed class RoleRequireAuthz : IAuthorizationGuard
    {
        public string? RequiredRole { get; set; } = "Admin";
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
        {
            if (RequiredRole != null && !string.Equals(role, RequiredRole, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires role {role}.");
            return Task.CompletedTask;
        }
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(RequiredRole);
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(RequiredRole != null);
    }

    [Fact]
    public async Task ListBookingsAsync_EnforcesAdminRole_AndExcludesNote()
    {
        var apptStoreMock = new Mock<IAppointmentStore>();
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1), 60, 500_000);
        apptStoreMock.Setup(s => s.ListAdminPagedAsync(null, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([appt], 1));

        var svc = new AdminBookingService(apptStoreMock.Object, new RoleRequireAuthz { RequiredRole = "Admin" });
        var (items, total) = await svc.ListBookingsAsync(Guid.NewGuid(), null, null, null, 1, 25, CancellationToken.None);

        total.Should().Be(1);
        items.Should().HaveCount(1);
        var item = items[0];
        item.AppointmentId.Should().Be(appt.Id);
        item.PriceListedVnd.Should().Be(500_000);
        // Note is completely omitted from AdminBookingSummaryResponse wire contract
        item.GetType().GetProperty("Note").Should().BeNull();
    }

    [Fact]
    public async Task GetBookingDetailAsync_EnforcesAdminRole_AndExcludesNote()
    {
        var apptStoreMock = new Mock<IAppointmentStore>();
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1), 60, 500_000);
        apptStoreMock.Setup(s => s.FindByIdAsync(appt.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var svc = new AdminBookingService(apptStoreMock.Object, new RoleRequireAuthz { RequiredRole = "Admin" });
        var detail = await svc.GetBookingDetailAsync(Guid.NewGuid(), appt.Id, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.AppointmentId.Should().Be(appt.Id);
        // Note is completely omitted from AdminBookingDetailResponse wire contract
        detail.GetType().GetProperty("Note").Should().BeNull();
    }

    [Fact]
    public async Task ListBookingsAsync_NonAdmin_ThrowsForbiddenException()
    {
        var apptStoreMock = new Mock<IAppointmentStore>();
        var svc = new AdminBookingService(apptStoreMock.Object, new RoleRequireAuthz { RequiredRole = "OtherRole" });

        var act = () => svc.ListBookingsAsync(Guid.NewGuid(), null, null, null, 1, 25, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
