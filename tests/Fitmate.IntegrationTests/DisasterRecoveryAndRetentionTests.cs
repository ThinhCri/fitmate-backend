using System.Data;
using Fitmate.Application.Common;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Common;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fitmate.IntegrationTests;

/// <summary>
/// BE-825: Integration tests cho Disaster Recovery và Retention trên PostgreSQL thật.
/// Kiểm chứng 5 Bất Biến Tài Chính, Bất Biến Quyền Riêng Tư (Tombstones), và DataProtection Canary Token.
/// </summary>
public class DisasterRecoveryAndRetentionTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public DisasterRecoveryAndRetentionTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task PostRestore_FinancialInvariants_Pass_On_Real_PostgreSql()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        // 1. Thêm fixture giao dịch hợp lệ vào PostgreSQL
        var appointmentId = Guid.NewGuid();
        var payment = Payment.CreatePending(appointmentId, PaymentMethod.Vnpay, 500_000, "https://pay.fitmate.local");
        payment.MarkPaid("VNPAY-TEST-TXN-825-001");
        payment.ApplyRefund(100_000);

        var adminId = Guid.NewGuid();
        var refund = Refund.Create(payment.Id, appointmentId, 100_000, "Customer cancellation within policy", adminId);
        refund.MarkSucceeded("VNPAY-REFUND-825-001");

        await db.Payments.AddAsync(payment);
        await db.Refunds.AddAsync(refund);
        await db.SaveChangesAsync();

        // 2. Chạy 5 câu lệnh SQL kiểm chứng bất biến
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.NonNegativeAmounts;
        var nonNegCount = (long)(await cmd1.ExecuteScalarAsync())!;
        nonNegCount.Should().Be(0, "Không được có payment hoặc refund mang giá trị âm");

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.RefundCapIntegrity;
        var capViolations = (long)(await cmd2.ExecuteScalarAsync())!;
        capViolations.Should().Be(0, "Số tiền hoàn tích lũy không được vượt quá số tiền đã trả");

        using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.CumulativeRefundIntegrity;
        var cumViolations = (long)(await cmd3.ExecuteScalarAsync())!;
        cumViolations.Should().Be(0, "Tổng các refund thành công không được vượt quá AmountVnd");

        using var cmd4 = conn.CreateCommand();
        cmd4.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.UniqueProviderTxnRef;
        var dupRefs = (long)(await cmd4.ExecuteScalarAsync())!;
        dupRefs.Should().Be(0, "Mã giao dịch ProviderTxnRef không được trùng lặp");

        using var cmd5 = conn.CreateCommand();
        cmd5.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.AppointmentPaymentReferentialIntegrity;
        var orphanAppointments = (long)(await cmd5.ExecuteScalarAsync())!;
        orphanAppointments.Should().Be(0, "Mọi PaymentId trên Appointment phải trỏ tới Payment hợp lệ");
    }

    [Fact]
    public async Task PostRestore_DeletionTombstones_Are_Preserved_And_Excluded_From_Active_Queries()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var deletedUserId = Guid.NewGuid();
        var deletedUser = new ApplicationUser
        {
            Id = deletedUserId,
            UserName = $"deleted_user_{Guid.NewGuid():N}@fitmate.local",
            Email = $"deleted_user_{Guid.NewGuid():N}@fitmate.local",
            FullName = "Tombstone User",
            DeletedAt = DateTime.UtcNow.AddDays(-2),
            Status = UserStatus.PendingDeletion
        };

        // Bỏ qua query filter để thêm và kiểm tra trực tiếp
        await db.Users.AddAsync(deletedUser);
        await db.SaveChangesAsync();

        // 1. Kiểm tra bằng câu lệnh SQL raw: Tombstone tồn tại vật lý để phục vụ audit/journal
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = PostRestoreInvariantChecker.SqlInvariantQueries.DeletedUsersCount;
        var deletedCount = (long)(await cmd.ExecuteScalarAsync())!;
        deletedCount.Should().BeGreaterThan(0, "Tài khoản bị xóa phải lưu trữ tombstone DeletedAt");

        // 2. Kiểm tra qua EF Core query bình thường: Global filter loại bỏ user đã xóa khỏi luồng nghiệp vụ
        var activeUser = await db.Users.FirstOrDefaultAsync(u => u.Id == deletedUserId);
        activeUser.Should().BeNull("Global query filter phải ẩn hoàn toàn người dùng đã bị xóa khỏi ứng dụng");
    }

    [Fact]
    public void DataProtection_CanaryToken_Protection_And_Unprotection_Succeeds_Via_AppServices()
    {
        using var scope = _factory.Services.CreateScope();
        var dpProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        // 1. Tạo canary token qua provider của ứng dụng
        var expectedPlaintext = "fitmate-production-dr-drill-canary";
        var ciphertext = DataProtectionCanaryVerifier.CreateCanaryCiphertext(
            dpProvider,
            purpose: PostRestoreInvariantChecker.DefaultCanaryPurpose,
            plaintext: expectedPlaintext);

        ciphertext.Should().NotBeNullOrWhiteSpace();

        // 2. Giải mã bằng provider của ứng dụng
        var (success, error) = DataProtectionCanaryVerifier.VerifyCanaryDecryption(
            dpProvider,
            ciphertext,
            purpose: PostRestoreInvariantChecker.DefaultCanaryPurpose,
            expectedPlaintext: expectedPlaintext);

        success.Should().BeTrue();
        error.Should().BeNull();
    }
}
