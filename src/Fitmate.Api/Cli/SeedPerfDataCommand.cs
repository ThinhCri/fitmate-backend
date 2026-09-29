using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Cli;

/// <summary>
/// CLI: dotnet run --project src/Fitmate.Api -- seed-perf-data [smoke|representative] [--allow-perf-seed]
///
/// Chốt chặn an toàn:
/// 1. Từ chối thực thi nếu môi trường là Production.
/// 2. Bắt buộc có biến môi trường PERF_SEED_ENABLED=true hoặc cờ --allow-perf-seed.
/// 3. Bắt buộc tên database đích phải chính xác là 'fitmate_perf'.
/// </summary>
public static class SeedPerfDataCommand
{
    public const string DefaultPassword = "PerfTestPass123!";
    public static readonly string[] RoleNames = { "Admin", "Member", "PT" };

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
    {
        Console.WriteLine("[seed-perf] === Fitmate Performance Dataset Seeder ===");

        // 1. Chốt chặn môi trường Production
        var env = configuration["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[seed-perf] LỖI BẢO MẬT: Cấm tuyệt đối chạy seed perf data trên môi trường Production.");
            return 1;
        }

        // 2. Chốt chặn cờ cho phép
        var allowFlag = args.Contains("--allow-perf-seed");
        var allowEnv = string.Equals(configuration["PERF_SEED_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (!allowFlag && !allowEnv)
        {
            Console.Error.WriteLine("[seed-perf] LỖI: Cần cờ '--allow-perf-seed' hoặc biến môi trường PERF_SEED_ENABLED=true để thực thi.");
            return 1;
        }

        // 3. Chốt chặn Database đích cụ thể (chỉ chấp nhận đúng DB fitmate_perf)
        var dbHost = configuration["DB_HOST"] ?? "localhost";
        var dbPort = configuration["DB_PORT"] ?? "5432";
        var dbName = configuration["DB_NAME"] ?? "";
        var dbUser = configuration["DB_USER"] ?? "fitmate";
        var dbPassword = configuration["DB_PASSWORD"] ?? throw new InvalidOperationException("DB_PASSWORD must be set");

        if (!string.Equals(dbName, "fitmate_perf", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"[seed-perf] LỖI BẢO MẬT: Database đích bắt buộc phải là 'fitmate_perf'. Giá trị hiện tại: '{dbName}'. Dừng ngay lập tức.");
            return 1;
        }

        var isSmoke = args.Contains("smoke", StringComparer.OrdinalIgnoreCase);
        var modeName = isSmoke ? "SMOKE" : "REPRESENTATIVE";
        Console.WriteLine($"[seed-perf] Chế độ: {modeName} | Database: {dbName} @ {dbHost}:{dbPort}");

        var connStr = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

        var services = new ServiceCollection();
        services.AddDbContext<FitmateDbContext>(opt => opt.UseNpgsql(connStr));
        services.AddIdentityCore<ApplicationUser>(opt =>
        {
            // Perf/UAT personas dùng mật khẩu tự sinh để test load; giữ yêu cầu phức tạp
            // hơn policy sản phẩm để dữ liệu test không trùng mẫu yếu của production.
            opt.Password.RequiredLength = PasswordPolicy.MinimumLength;
            opt.Password.RequireDigit = true;
            opt.Password.RequireUppercase = true;
            opt.Password.RequireLowercase = true;
            opt.Password.RequireNonAlphanumeric = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<FitmateDbContext>();

        services.AddLogging(b => b.AddConsole());
        await using var sp = services.BuildServiceProvider();

        await using var db = sp.GetRequiredService<FitmateDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Ensure roles
        foreach (var role in RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role });
        }

        int targetMembers = isSmoke ? 10 : 600;
        int targetPts = isSmoke ? 5 : 60;
        int designatedUsersWithManyBookings = isSmoke ? 2 : 10;
        int bookingsPerDesignatedUser = isSmoke ? 10 : 60;

        var userCount = await db.Users.CountAsync();
        var bookingCount = await db.Appointments.CountAsync();
        bool force = args.Contains("--force");

        if (!force && userCount >= (targetMembers + targetPts) && bookingCount >= (isSmoke ? 30 : 900))
        {
            Console.WriteLine($"[seed-perf] Dữ liệu perf đã đầy đủ ({userCount} users, {bookingCount} bookings). Bỏ qua seed.");
            return 0;
        }

        Console.WriteLine($"[seed-perf] Đang tạo {targetPts} PTs...");
        var ptUsers = new List<ApplicationUser>();
        var ptServices = new List<Service>();
        var baseUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 9, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= targetPts; i++)
        {
            var ptEmail = $"perf_pt_{i:D3}@fitmate.vn";
            var existingPt = await db.Users.FirstOrDefaultAsync(u => u.Email == ptEmail);
            ApplicationUser pt;

            if (existingPt != null)
            {
                pt = existingPt;
                if (!await userManager.IsInRoleAsync(pt, "PT"))
                    await userManager.AddToRoleAsync(pt, "PT");
            }
            else
            {
                pt = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = ptEmail,
                    Email = ptEmail,
                    FullName = $"Perf PT {i:D3}",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var res = await userManager.CreateAsync(pt, DefaultPassword);
                if (!res.Succeeded)
                {
                    Console.Error.WriteLine($"[seed-perf] Tạo PT {ptEmail} lỗi: {string.Join("; ", res.Errors.Select(e => e.Description))}");
                    return 1;
                }
                await userManager.AddToRoleAsync(pt, "PT");
            }
            ptUsers.Add(pt);

            // PtProfile
            if (!await db.PtProfiles.AnyAsync(p => p.UserId == pt.Id))
            {
                var profile = PtProfile.CreateDraft(pt.Id, "Strength & Conditioning", 5, 350000, "Certified fitness trainer for load testing.");
                profile.ApplyRating(4.9m, 25);
                db.PtProfiles.Add(profile);
            }

            // Approved Certificate so trainer shows up in /trainers marketplace queries
            if (!await db.Certificates.AnyAsync(c => c.PtUserId == pt.Id))
            {
                var cert = Certificate.Submit(pt.Id, "NASM Certified Personal Trainer", "NASM", $"NASM-PERF-{i:D3}", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)), null);
                cert.Approve(Guid.NewGuid());
                db.Certificates.Add(cert);
            }

            // Service
            var existingService = await db.Services.FirstOrDefaultAsync(s => s.PtUserId == pt.Id);
            if (existingService != null)
            {
                ptServices.Add(existingService);
            }
            else
            {
                var service = Service.Create(pt.Id, "1-on-1 PT Session", "High intensity personal training session", 350000, 60);
                db.Services.Add(service);
                ptServices.Add(service);
            }

            // Availability Slots: 10 slots
            if (!await db.AvailabilitySlots.AnyAsync(s => s.PtUserId == pt.Id))
            {
                for (int s = 1; s <= 10; s++)
                {
                    var slotTime = baseUtc.AddDays(s);
                    var slot = AvailabilitySlot.Create(pt.Id, slotTime, "Asia/Ho_Chi_Minh", 60);
                    db.AvailabilitySlots.Add(slot);
                }
            }
        }
        await db.SaveChangesAsync();

        Console.WriteLine($"[seed-perf] Đang tạo {targetMembers} Members...");
        var memberUsers = new List<ApplicationUser>();
        for (int i = 1; i <= targetMembers; i++)
        {
            var memberEmail = $"perf_member_{i:D3}@fitmate.vn";
            var existingMember = await db.Users.FirstOrDefaultAsync(u => u.Email == memberEmail);
            ApplicationUser member;

            if (existingMember != null)
            {
                member = existingMember;
                if (!await userManager.IsInRoleAsync(member, "Member"))
                    await userManager.AddToRoleAsync(member, "Member");
            }
            else
            {
                member = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = memberEmail,
                    Email = memberEmail,
                    FullName = $"Perf Member {i:D3}",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var res = await userManager.CreateAsync(member, DefaultPassword);
                if (!res.Succeeded)
                {
                    Console.Error.WriteLine($"[seed-perf] Tạo Member {memberEmail} lỗi: {string.Join("; ", res.Errors.Select(e => e.Description))}");
                    return 1;
                }
                await userManager.AddToRoleAsync(member, "Member");
            }
            memberUsers.Add(member);
        }

        Console.WriteLine($"[seed-perf] Đang tạo Bookings phân bổ theo từng User (đảm bảo trang sâu có dữ liệu)...");
        var appointments = new List<Appointment>();
        var defaultPt = ptUsers[0];
        var defaultSvc = ptServices[0];

        // 1. Seed cho nhóm designated members (mỗi người 60 bookings) để trang 5x10 luôn đủ dữ liệu
        for (int m = 0; m < designatedUsersWithManyBookings && m < memberUsers.Count; m++)
        {
            var user = memberUsers[m];
            for (int b = 1; b <= bookingsPerDesignatedUser; b++)
            {
                var startUtc = DateTime.UtcNow.AddDays(-b);
                var appt = Appointment.CreateHold(user.Id, defaultPt.Id, defaultSvc.Id, startUtc, startUtc, 60, defaultSvc.PriceVnd);
                appt.AttachPayment(Guid.NewGuid(), defaultSvc.PriceVnd, 0, 0.10m, (long)(defaultSvc.PriceVnd * 0.10m), (long)(defaultSvc.PriceVnd * 0.90m), "v1");
                appt.ConfirmByPt();
                appointments.Add(appt);
            }
        }

        // 2. Seed thêm bookings cho các members còn lại để tổng cộng đạt ~1000 bookings (hoặc ~50 ở smoke)
        int remainingBookings = isSmoke ? 30 : 400;
        for (int i = 0; i < remainingBookings; i++)
        {
            var user = memberUsers[i % memberUsers.Count];
            var pt = ptUsers[i % ptUsers.Count];
            var svc = ptServices[i % ptServices.Count];
            var startUtc = DateTime.UtcNow.AddDays(-(i + 1));
            var appt = Appointment.CreateHold(user.Id, pt.Id, svc.Id, startUtc, startUtc, 60, svc.PriceVnd);
            appt.AttachPayment(Guid.NewGuid(), svc.PriceVnd, 0, 0.10m, (long)(svc.PriceVnd * 0.10m), (long)(svc.PriceVnd * 0.90m), "v1");
            appointments.Add(appt);
        }

        await db.Appointments.AddRangeAsync(appointments);
        await db.SaveChangesAsync();

        // 3. Seed Meal Logs và Workout Sessions
        Console.WriteLine($"[seed-perf] Đang tạo Meal Logs & Workout Sessions...");
        var mealLogs = new List<MealLog>();
        var workoutSessions = new List<WorkoutSession>();

        int totalLogs = isSmoke ? 50 : 2000;
        for (int i = 0; i < totalLogs; i++)
        {
            var user = memberUsers[i % memberUsers.Count];
            var meal = MealLog.Create(user.Id, $"Perf Healthy Meal {i}", null, 550, 40, 60, 15, 1.0m, MealSource.Manual, null, true, DateTime.UtcNow.AddHours(-i));
            mealLogs.Add(meal);

            if (i < (isSmoke ? 30 : 1000))
            {
                var session = WorkoutSession.Create(user.Id, null, $"client-perf-{i}-{Guid.NewGuid():N}", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)), "Monday", 45, 350, 6, WorkoutSessionStatus.Completed, 18, 18);
                workoutSessions.Add(session);
            }
        }

        await db.MealLogs.AddRangeAsync(mealLogs);
        await db.WorkoutSessions.AddRangeAsync(workoutSessions);
        await db.SaveChangesAsync();

        Console.WriteLine($"[seed-perf] HOÀN TẤT: Đã seed thành công {memberUsers.Count} members, {ptUsers.Count} PTs, {appointments.Count} bookings, {mealLogs.Count} meal logs, {workoutSessions.Count} workouts vào '{dbName}'.");
        return 0;
    }
}
