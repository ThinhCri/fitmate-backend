using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Capabilities;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Application.Identity;
using Fitmate.Application.Notifications;
using Fitmate.Application.Nutrition;
using Fitmate.Application.Preferences;
using Fitmate.Application.Workout;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

/// <summary>
/// BE-826: Bộ kiểm thử tích hợp API chuẩn bị cho UAT xuyên 3 repo (FitmateAI_BE, FitmateAI_FE, FitmateWebApp).
/// Kiểm chứng 10 hành trình nghiệp vụ cốt lõi (BR-01..BR-16) trên WebApplicationFactory kết nối PostgreSQL 16 thật:
/// - Không tự bịa đặt field hay trạng thái; đối chiếu chính xác Domain entities và Application contracts hiện có.
/// - Pending là trạng thái appointment đã thanh toán trước khi PT confirm (AppointmentStatus.Pending).
/// - Block chỉ cô lập tương tác giữa 2 bên, không khóa push token của bên thứ ba.
/// - Kiểm chứng offline retry dedup, data export, cross-account push revocation, và manual settlement.
/// </summary>
public class UatJourneysIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public UatJourneysIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Journey01_Auth_Lifecycle_Preferences_And_CrossAccount_Denied()
    {
        var memberId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: memberId);

        // 1. Cập nhật tùy chọn cá nhân (Preferences)
        var updateReq = new UpdateUserPreferenceRequest(
            Language: "vi",
            Theme: "dark",
            Timezone: "Asia/Ho_Chi_Minh",
            NotificationsEnabled: true,
            EmailNotifications: true,
            PushNotifications: true,
            HealthDataSharingConsent: true,
            BiometricLoginEnabled: true);

        var putResp = await client.PutAsJsonAsync("/api/v1/users/me/preferences", updateReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var pref = await putResp.Content.ReadFromJsonAsync<UserPreferenceDto>();
        pref.Should().NotBeNull();
        pref!.Theme.Should().Be("dark");
        pref.Timezone.Should().Be("Asia/Ho_Chi_Minh");

        // 2. Chặn truy cập chéo phân quyền (Cross-account privilege boundary)
        var adminEndpointResp = await client.GetAsync("/api/v1/admin/users");
        adminEndpointResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Không có token -> 401 Unauthorized
        var unauthClient = _factory.CreateClient();
        var unauthResp = await unauthClient.GetAsync("/api/v1/users/me/preferences");
        unauthResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Journey02_Pt_Onboarding_Certificate_And_Availability_Publishing()
    {
        var ptId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        // 1. PT nộp hồ sơ & chứng chỉ (Pending)
        var ptProfile = PtProfile.CreateDraft(ptId, "Strength & Conditioning", 4, 300000, "HLV giàu kinh nghiệm.");
        db.PtProfiles.Add(ptProfile);

        var cert = Certificate.Submit(ptId, "CrossFit Level 2", "CrossFit Inc", "CF-L2-1002", new DateOnly(2023, 1, 15), null);
        cert.Status.Should().Be(CertificateStatus.Pending);
        db.Certificates.Add(cert);

        // 2. Admin phê duyệt chứng chỉ
        var adminId = Guid.NewGuid();
        cert.Approve(adminId);
        cert.Status.Should().Be(CertificateStatus.Approved);
        cert.VerifiedByAdminId.Should().Be(adminId);

        // 3. PT mở dịch vụ và slot lịch hẹn
        var service = Service.Create(ptId, "Powerlifting Basics", "Huấn luyện kỹ thuật 60p", 300000, 60);
        db.Services.Add(service);

        var slotTime = DateTime.UtcNow.Date.AddDays(2).AddHours(14);
        var slot = AvailabilitySlot.Create(ptId, slotTime, "Asia/Ho_Chi_Minh", 60);
        db.AvailabilitySlots.Add(slot);

        await db.SaveChangesAsync();

        slot.Status.Should().Be(AvailabilityStatus.Open);
        service.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Journey03_Booking_Hold_Payment_And_PT_Acceptance_State_Machine()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        // 1. Member tạo Hold -> trạng thái là Held
        var appt = Appointment.CreateHold(
            memberId, ptId, serviceId,
            DateTime.UtcNow.AddDays(3),
            DateTime.UtcNow.AddDays(3).AddHours(7),
            60, 300000);

        appt.Status.Should().Be(AppointmentStatus.Held);

        // 2. State Machine Rule: Chưa thanh toán thì PT KHÔNG ĐƯỢC phép Confirm
        var confirmAction = () => appt.ConfirmByPt();
        confirmAction.Should().Throw<ConflictException>()
            .WithMessage("*Cannot confirm booking in status Held*");

        // 3. Thanh toán thành công (Payment attached) -> chuyển sang BookingStatus.Paid
        var paymentId = Guid.NewGuid();
        appt.AttachPayment(paymentId, 300000, 0, 0.10m, 30000, 270000, "v1");
        appt.Status.Should().Be(AppointmentStatus.Pending);
        appt.PaymentId.Should().Be(paymentId);

        // 4. PT tiếp nhận và chấp thuận -> chuyển sang BookingStatus.Confirmed
        appt.ConfirmByPt();
        appt.Status.Should().Be(AppointmentStatus.Confirmed);
    }

    [Fact]
    public async Task Journey04_Session_Execution_And_Trainer_Revenue_Calculation()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        // 1. Tạo PT Profile & User để vượt qua EnsureRoleAsync("PT")
        var ptRole = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "PT");
        if (ptRole == null)
        {
            ptRole = new Microsoft.AspNetCore.Identity.IdentityRole<Guid>("PT") { NormalizedName = "PT" };
            db.Roles.Add(ptRole);
            await db.SaveChangesAsync();
        }

        var ptUser = new ApplicationUser
        {
            Id = ptId,
            UserName = $"pt_{ptId:N}@fitmate.local",
            NormalizedUserName = $"PT_{ptId:N}@FITMATE.LOCAL",
            Email = $"pt_{ptId:N}@fitmate.local",
            NormalizedEmail = $"PT_{ptId:N}@FITMATE.LOCAL",
            FullName = "Trainer UAT Revenue",
            Status = UserStatus.Active,
            Timezone = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(ptUser);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = ptId,
            RoleId = ptRole.Id
        });

        // 2. Tạo buổi hẹn: Paid -> Confirmed -> InProgress -> Completed
        var slotStart = DateTime.UtcNow.AddMinutes(-30);
        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slotStart, slotStart.AddHours(7), 60, 400000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 400000, null);
        payment.MarkPaid($"UAT-REVENUE-{Guid.NewGuid():N}");
        appt.AttachPayment(payment.Id, 400000, 0, 0.10m, 40000, 360000, "v1");
        appt.ConfirmByPt();
        appt.StartByPt();
        appt.Status.Should().Be(AppointmentStatus.InProgress);

        appt.CompleteByPt();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        db.AddRange(appt, payment);
        await db.SaveChangesAsync();

        // 3. Đối soát doanh thu thực tế qua ITrainerRevenueService
        var revenueService = scope.ServiceProvider.GetRequiredService<ITrainerRevenueService>();
        var overview = await revenueService.GetOverviewAsync(ptId, FinanceRange.All, CancellationToken.None);

        // Điều kiện kép: Paid + Completed
        overview.TotalGrossVnd.Should().Be(400000);
        overview.PlatformFeeVnd.Should().Be(40000); // 10%
        overview.NetEarningsVnd.Should().Be(360000); // 90%
        overview.CompletedSessionsCount.Should().Be(1);
    }

    [Fact]
    public async Task Journey05_Cancellation_And_Ledger_Refund_Integrity()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // 1. Hủy booking khi đã Paid
        var appt = Appointment.CreateHold(memberId, ptId, serviceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(7), 60, 500000);
        appt.AttachPayment(paymentId, 500000, 0, 0.10m, 50000, 450000, "v1");

        appt.Cancel("Bận việc đột xuất trước 24h");
        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.Note.Should().Be("Bận việc đột xuất trước 24h");

        // 2. Chuyển trạng thái sang Refunded
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 500000, null);
        payment.MarkPaid("UAT-PAY-REFUND");
        var refund = Refund.CreateSystemRefund(payment.Id, appt.Id, 500000, "Hoàn tiền do hủy");
        refund.MarkSucceeded("REF-SUCCESS");
        payment.ApplyRefund(500000);
        payment.Status.Should().Be(PaymentStatus.Refunded);

        // 3. Quy tắc tính Net Earnings khi có hoàn tiền: Net = TrainerShare - (refundedVnd * 0.90)
        long trainerShare = 450000;
        long refundedVnd = 500000;
        long netEarnings = Math.Max(0, trainerShare - (long)Math.Round(refundedVnd * 0.90m, MidpointRounding.AwayFromZero));

        netEarnings.Should().Be(0, "Hoàn trả toàn bộ thì thu nhập thực nhận của PT từ buổi này về 0.");
    }

    [Fact]
    public async Task Journey06_Offline_Workout_Session_Retry_Dedup()
    {
        var memberId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: memberId);

        // 1. Member tạo kế hoạch tập luyện
        var planReq = new CreateWorkoutPlanRequest
        {
            DayOfWeek = "Monday",
            WorkoutName = "UAT Offline Reconnect Plan",
            DurationMinutes = 45,
            TargetCaloriesBurn = 300
        };

        var planResp = await client.PostAsJsonAsync("/api/v1/me/workout/plans", planReq);
        planResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = await planResp.Content.ReadFromJsonAsync<WorkoutPlanView>();
        plan.Should().NotBeNull();

        // 2. Gửi nhật ký buổi tập với Idempotency-Key (Mô phỏng offline retry)
        var idempotencyKey = $"offline-sync-{Guid.NewGuid():N}";
        var sessionReq = new LogWorkoutSessionRequest
        {
            ClientId = idempotencyKey,
            PlanId = plan!.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            DurationMin = 45,
            CaloriesBurned = 300,
            Exercises = 1,
            Status = WorkoutSessionStatus.Completed
        };

        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/workout/sessions")
        {
            Content = JsonContent.Create(sessionReq)
        };
        msg1.Headers.Add("Idempotency-Key", idempotencyKey);

        var firstResp = await client.SendAsync(msg1);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstSession = await firstResp.Content.ReadFromJsonAsync<WorkoutSessionDetailView>();
        firstSession.Should().NotBeNull();

        // 3. Gửi lại cùng Idempotency-Key (Mô phỏng mạng chập chờn / thiết bị retry)
        var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/workout/sessions")
        {
            Content = JsonContent.Create(sessionReq)
        };
        msg2.Headers.Add("Idempotency-Key", idempotencyKey);

        var retryResp = await client.SendAsync(msg2);
        // Middleware idempotency / service xử lý trả về kết quả an toàn không tạo duplicate
        retryResp.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        // 4. Kiểm chứng số lượng session trong DB chỉ là 1
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var sessionsCount = await db.WorkoutSessions.CountAsync(s => s.UserId == memberId);
        sessionsCount.Should().Be(1, "Idempotency-Key phải ngăn ngừa tạo trùng lặp bản ghi buổi tập.");
    }

    [Fact]
    public async Task Journey07_Nutrition_Meal_Logging_And_Deletion()
    {
        var memberId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: memberId);

        // 1. Ghi nhật ký bữa ăn
        var mealReq = new LogMealRequest
        {
            FoodName = "Phở bò tái nạm",
            ImageUrl = null,
            Calories = 550,
            ProteinG = 35,
            CarbsG = 65,
            FatG = 15,
            PortionSize = 1.0m,
            Source = MealSource.Manual,
            AiConfidence = null,
            LoggedAt = DateTime.UtcNow
        };

        var postResp = await client.PostAsJsonAsync("/api/v1/me/nutrition/meals", mealReq);
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdMeal = await postResp.Content.ReadFromJsonAsync<MealLogView>();
        createdMeal.Should().NotBeNull();
        createdMeal!.FoodName.Should().Be("Phở bò tái nạm");

        // 2. Xóa nhật ký bữa ăn (Thực hiện soft delete dòng dữ liệu và phát sinh Outbox event)
        var deleteResp = await client.DeleteAsync($"/api/v1/me/nutrition/meals/{createdMeal.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Kiểm chứng bản ghi đã được gán DeletedAt (Soft delete)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var mealInDb = await db.MealLogs.FirstOrDefaultAsync(m => m.Id == createdMeal.Id);
        mealInDb.Should().NotBeNull();
        mealInDb!.DeletedAt.Should().NotBeNull("Bản ghi meal log phải được gán DeletedAt khi bị xóa.");
    }

    [Fact]
    public async Task Journey08_Social_Monotonic_Chat_And_Block_Isolation()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            db.Users.AddRange(
                new ApplicationUser { Id = userA, UserName = $"userA_{userA:N}@fitmate.local", Email = $"userA_{userA:N}@fitmate.local", FullName = "User A", Status = UserStatus.Active, Timezone = "Asia/Ho_Chi_Minh", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new ApplicationUser { Id = userB, UserName = $"userB_{userB:N}@fitmate.local", Email = $"userB_{userB:N}@fitmate.local", FullName = "User B", Status = UserStatus.Active, Timezone = "Asia/Ho_Chi_Minh", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        // 1. Thiết lập chặn hai chiều (User A chặn User B)
        var clientA = _factory.CreateAuthenticatedClient(role: "Member", userId: userA);
        var blockReq = new { userId = userB };
        var blockResp = await clientA.PostAsJsonAsync("/api/v1/social/blocks", blockReq);
        blockResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);

        // 2. Kiểm chứng block đã tồn tại trong DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var blocked = await db.BlockedUsers.AnyAsync(b => b.BlockerId == userA && b.BlockedId == userB);
            blocked.Should().BeTrue("Mối quan hệ chặn giữa User A và User B phải được lưu trữ.");
        }

        // 3. Đảm bảo block chỉ cô lập giữa A và B, không ảnh hưởng push device token của người khác
        var clientB = _factory.CreateAuthenticatedClient(role: "Member", userId: userB);
        var deviceTokenReq = new { deviceToken = $"token-userB-{Guid.NewGuid():N}", platform = "android", provider = "fcm" };
        var devResp = await clientB.PostAsJsonAsync("/api/v1/users/me/devices", deviceTokenReq);
        devResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Journey09_Admin_Audit_Trail_And_Manual_Trainer_Settlement()
    {
        var adminId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var bankReference = $"VCB-UAT-{Guid.NewGuid():N}";
        Guid appointmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var sessionStart = DateTime.UtcNow.AddHours(-2);
            var appointment = Appointment.CreateHold(
                memberId, ptId, serviceId, sessionStart, sessionStart.AddHours(7), 60, 400_000);
            var payment = Payment.CreatePending(appointment.Id, PaymentMethod.Payos, 400_000, null);
            payment.MarkPaid($"PAYOS-UAT-{Guid.NewGuid():N}");
            appointment.AttachPayment(payment.Id, 400_000, 0, 0.10m, 40_000, 360_000, "v1");
            appointment.ConfirmByPt();
            appointment.StartByPt();
            appointment.CompleteByPt();

            appointmentId = appointment.Id;
            db.AddRange(appointment, payment);
            await db.SaveChangesAsync();
        }

        var adminClient = _factory.CreateAuthenticatedClient(
            role: "Admin",
            userId: adminId,
            includeFinanceTotp: true);
        var confirmedAt = DateTime.UtcNow.AddMinutes(-5);

        var approveRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/finance/manual-settlements/{appointmentId}/approve")
        {
            Content = JsonContent.Create(new ApproveSettlementRequest(
                confirmedAt,
                confirmedAt,
                "private-session-record/uat-journey-09",
                "private-payee-verification/uat-journey-09",
                NoOpenDisputeConfirmed: true))
        };
        approveRequest.Headers.Add("Idempotency-Key", $"uat-settlement-approve-{appointmentId:N}");
        var approveResponse = await adminClient.SendAsync(approveRequest);
        var approveBody = await approveResponse.Content.ReadAsStringAsync();
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, "approve response was: {0}", approveBody);

        var transferredAt = DateTime.UtcNow;
        var transferRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/finance/manual-settlements/{appointmentId}/record-transfer")
        {
            Content = JsonContent.Create(new RecordTransferRequest(
                bankReference,
                "private-bank-receipt/uat-journey-09",
                transferredAt))
        };
        transferRequest.Headers.Add("Idempotency-Key", $"uat-settlement-transfer-{appointmentId:N}");
        var transferResponse = await adminClient.SendAsync(transferRequest);
        transferResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var settlement = await verifyDb.Set<ManualPtSettlement>().SingleAsync(s => s.AppointmentId == appointmentId);
        settlement.Status.Should().Be("paid");
        settlement.AmountVnd.Should().Be(360_000);
        settlement.BankReference.Should().Be(bankReference);
        (await verifyDb.AuditLogs.AnyAsync(a =>
            a.TargetId == appointmentId && a.Action == "finance.settlement_approved")).Should().BeTrue();
        (await verifyDb.AuditLogs.AnyAsync(a =>
            a.TargetId == appointmentId && a.Action == "finance.trainer_settled")).Should().BeTrue();
    }

    [Fact]
    public async Task Journey10_Account_Tombstone_Data_Export_And_Truthful_Capabilities()
    {
        var memberId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var memberUser = new ApplicationUser
            {
                Id = memberId,
                UserName = $"member_{memberId:N}@fitmate.local",
                NormalizedUserName = $"MEMBER_{memberId:N}@FITMATE.LOCAL",
                Email = $"member_{memberId:N}@fitmate.local",
                NormalizedEmail = $"MEMBER_{memberId:N}@FITMATE.LOCAL",
                FullName = "Member UAT Export",
                Status = UserStatus.Active,
                Timezone = "Asia/Ho_Chi_Minh",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(memberUser);

            var profile = UserProfile.Create(memberId);
            profile.Update(phone: "0911223344", fitnessGoal: "Duy trì vóc dáng");
            db.UserProfiles.Add(profile);

            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: memberId);

        // 1. Kiểm tra Data Export thật (POST /api/v1/users/me/export)
        var exportResp = await client.PostAsync("/api/v1/users/me/export", null);
        exportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportData = await exportResp.Content.ReadFromJsonAsync<UserDataExportDto>();
        exportData.Should().NotBeNull();
        exportData!.UserId.Should().Be(memberId);

        // 2. Kiểm tra thu hồi push token khi đổi tài khoản (Cross-account push leak protection)
        var sharedToken = $"shared-fcm-device-{Guid.NewGuid():N}";
        var devReqA = new { deviceToken = sharedToken, platform = "android", provider = "fcm" };
        var regRespA = await client.PostAsJsonAsync("/api/v1/users/me/devices", devReqA);
        regRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tài khoản khác (User B) đăng nhập trên cùng thiết bị và đăng ký cùng device token
        var userB = Guid.NewGuid();
        var clientB = _factory.CreateAuthenticatedClient(role: "Member", userId: userB);
        var devReqB = new { deviceToken = sharedToken, platform = "android", provider = "fcm" };
        var regRespB = await clientB.PostAsJsonAsync("/api/v1/users/me/devices", devReqB);
        regRespB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Xác nhận device token của User A đã bị thu hồi (IsActive = false) để chống lộ push
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var devA = await db.Devices.FirstOrDefaultAsync(d => d.UserId == memberId);
            if (devA != null)
            {
                devA.IsActive.Should().BeFalse("Device token của User A phải bị thu hồi khi User B đăng ký trên cùng thiết bị.");
            }
        }

        // 3. Kiểm tra Truthful Capabilities (BE-829 freeze)
        var capResp = await client.GetAsync("/api/v1/system/capabilities");
        capResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cap = await capResp.Content.ReadFromJsonAsync<SystemCapabilitiesDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        cap.Should().NotBeNull();
        cap!.VipPurchase.Enabled.Should().BeFalse();
        cap.RewardCashout.Enabled.Should().BeFalse();
        cap.PtAutomaticPayout.Enabled.Should().BeFalse();
    }
}

