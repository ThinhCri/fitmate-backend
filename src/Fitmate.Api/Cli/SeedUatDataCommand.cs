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

namespace Fitmate.Api.Cli;

/// <summary>
/// CLI: dotnet run --project src/Fitmate.Api -- seed-uat-data [--allow-uat-seed]
///
/// Chốt chặn an toàn (BE-826):
/// 1. Từ chối thực thi nếu môi trường là Production.
/// 2. Bắt buộc có biến môi trường UAT_SEED_ENABLED=true hoặc cờ --allow-uat-seed.
/// 3. Bắt buộc tên database đích phải thuộc danh sách cho phép UAT/staging ('fitmate_uat', 'fitmate_staging'
///    hoặc khai báo trong ALLOWED_UAT_DB_NAMES).
/// 4. Không nhận password qua command-line arguments (tránh lộ qua ps/history); đọc qua STDIN hoặc UAT_SEED_PASSWORD.
/// 5. Tính lũy kế (Idempotent): nếu persona đã tồn tại, bảo toàn tài khoản, không reset mật khẩu, không đè MFA.
/// </summary>
public static class SeedUatDataCommand
{
    public static readonly string[] RoleNames = { "Admin", "Member", "PT" };

    private static readonly string[] DefaultAllowedDatabases = { "fitmate_uat", "fitmate_staging" };

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
    {
        Console.WriteLine("[seed-uat] === Fitmate UAT Dataset Seeder (BE-826) ===");

        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        // 1. Chốt chặn bảo mật: Cấm truyền password qua argv
        if (args.Any(a => a.StartsWith("--password", StringComparison.OrdinalIgnoreCase) || a.Equals("-p", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("[seed-uat] LỖI BẢO MẬT: Cấm tuyệt đối truyền password qua command-line arguments (dễ lộ qua process table và shell history).");
            Console.Error.WriteLine("[seed-uat] Hãy nhập password qua STDIN sau dấu nhắc hoặc truyền qua biến môi trường bảo mật UAT_SEED_PASSWORD.");
            return 1;
        }

        // 2. Chốt chặn môi trường Production
        var env = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[seed-uat] LỖI BẢO MẬT: Cấm tuyệt đối chạy seed UAT data trên môi trường Production.");
            return 1;
        }

        // 3. Chốt chặn cờ chỉ định
        var allowFlag = args.Contains("--allow-uat-seed");
        var allowEnv = string.Equals(configuration["UAT_SEED_ENABLED"] ?? Environment.GetEnvironmentVariable("UAT_SEED_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        if (!allowFlag && !allowEnv)
        {
            Console.Error.WriteLine("[seed-uat] LỖI: Cần cờ '--allow-uat-seed' hoặc biến môi trường UAT_SEED_ENABLED=true để thực thi.");
            return 1;
        }

        // 4. Chốt chặn Database đích (Database Whitelist Guard)
        var dbHost = configuration["DB_HOST"] ?? Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var dbPort = configuration["DB_PORT"] ?? Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var dbName = configuration["DB_NAME"] ?? Environment.GetEnvironmentVariable("DB_NAME") ?? "";
        var dbUser = configuration["DB_USER"] ?? Environment.GetEnvironmentVariable("DB_USER") ?? "fitmate";
        var dbPassword = configuration["DB_PASSWORD"] ?? Environment.GetEnvironmentVariable("DB_PASSWORD")
            ?? throw new InvalidOperationException("DB_PASSWORD must be set");

        var customAllowedList = (configuration["ALLOWED_UAT_DB_NAMES"] ?? Environment.GetEnvironmentVariable("ALLOWED_UAT_DB_NAMES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var isDbAllowed = DefaultAllowedDatabases.Contains(dbName, StringComparer.OrdinalIgnoreCase)
            || customAllowedList.Contains(dbName, StringComparer.OrdinalIgnoreCase);

        if (!isDbAllowed)
        {
            Console.Error.WriteLine($"[seed-uat] LỖI BẢO MẬT: Database đích '{dbName}' không nằm trong whitelist UAT/Staging ({string.Join(", ", DefaultAllowedDatabases)}).");
            Console.Error.WriteLine("[seed-uat] Để cho phép DB khác, thiết lập biến môi trường ALLOWED_UAT_DB_NAMES=db_name1,db_name2.");
            return 1;
        }

        // 5. Tiếp nhận mật khẩu khởi tạo cho persona mới (không log ra màn hình)
        string? personaPassword = configuration["UAT_SEED_PASSWORD"]
            ?? Environment.GetEnvironmentVariable("UAT_SEED_PASSWORD");

        if (string.IsNullOrWhiteSpace(personaPassword))
        {
            Console.Write($"[seed-uat] Nhập mật khẩu khởi tạo cho các tài khoản UAT mới ({PasswordPolicy.MinimumLength}-{PasswordPolicy.MaximumLength} ký tự): ");
            personaPassword = Console.ReadLine();
        }

        if (!PasswordPolicy.IsAcceptable(personaPassword))
        {
            Console.Error.WriteLine($"[seed-uat] LỖI: Mật khẩu khởi tạo không hợp lệ. Độ dài bắt buộc {PasswordPolicy.MinimumLength}-{PasswordPolicy.MaximumLength} ký tự.");
            return 1;
        }

        Console.WriteLine($"[seed-uat] Kết nối Database UAT: {dbName} @ {dbHost}:{dbPort} | Môi trường: {env}");

        var connStr = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddLogging();
        services.AddDbContext<FitmateDbContext>(opt => opt.UseNpgsql(connStr));
        services.AddIdentityCore<ApplicationUser>(opt =>
        {
            opt.Password.RequiredLength = PasswordPolicy.MinimumLength;
            opt.Password.RequireDigit = true;
            opt.Password.RequireUppercase = false;
            opt.Password.RequireLowercase = false;
            opt.Password.RequireNonAlphanumeric = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<FitmateDbContext>()
        .AddDefaultTokenProviders();

        await using var sp = services.BuildServiceProvider();
        await using var db = sp.GetRequiredService<FitmateDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Ensure roles
        foreach (var roleName in RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var r = await roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
                if (!r.Succeeded)
                {
                    Console.Error.WriteLine($"[seed-uat] Lỗi tạo role '{roleName}': {string.Join("; ", r.Errors.Select(e => e.Description))}");
                    return 1;
                }
            }
        }

        // Persona definitions (Synthetic UAT)
        var personas = new[]
        {
            new PersonaSpec("uat.admin@fitmate.local", "UAT Admin", "Admin"),
            new PersonaSpec("uat.pt.verified@fitmate.local", "UAT Coach Sang", "PT"),
            new PersonaSpec("uat.pt.pending@fitmate.local", "UAT Coach Minh", "PT"),
            new PersonaSpec("uat.member@fitmate.local", "UAT Member Lan", "Member")
        };

        foreach (var spec in personas)
        {
            var existing = await userManager.FindByEmailAsync(spec.Email);
            if (existing != null)
            {
                var inRole = await userManager.IsInRoleAsync(existing, spec.Role);
                if (!inRole)
                {
                    Console.Error.WriteLine($"[seed-uat] CẢNH BÁO: Tài khoản '{spec.Email}' đã tồn tại nhưng không có role '{spec.Role}'. Không tự nâng quyền.");
                }
                else
                {
                    Console.WriteLine($"[seed-uat] Giữ nguyên persona '{spec.Email}' (Role={spec.Role}, Id={existing.Id}). Không reset mật khẩu hoặc MFA.");
                    await EnsureDomainDataAsync(existing.Id, spec.Email, db);
                }
                continue;
            }

            // Create new persona
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = spec.Email.ToLowerInvariant(),
                Email = spec.Email.ToLowerInvariant(),
                NormalizedEmail = spec.Email.ToUpperInvariant(),
                NormalizedUserName = spec.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                FullName = spec.FullName,
                Status = UserStatus.Active,
                Timezone = "Asia/Ho_Chi_Minh",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createRes = await userManager.CreateAsync(user, personaPassword!);
            if (!createRes.Succeeded)
            {
                Console.Error.WriteLine($"[seed-uat] Lỗi tạo user '{spec.Email}': {string.Join("; ", createRes.Errors.Select(e => e.Description))}");
                return 1;
            }

            await userManager.AddToRoleAsync(user, spec.Role);
            Console.WriteLine($"[seed-uat] Đã tạo mới persona '{spec.Email}' (Role={spec.Role}, Id={user.Id}).");

            // Seed domain specifics
            if (spec.Email == "uat.admin@fitmate.local")
            {
                // Configure MFA readiness for Admin persona
                await userManager.ResetAuthenticatorKeyAsync(user);
                await userManager.SetTwoFactorEnabledAsync(user, true);
                Console.WriteLine($"[seed-uat] Đã thiết lập sẵn TwoFactorEnabled=true cho Admin '{spec.Email}'.");
            }
            else
            {
                await EnsureDomainDataAsync(user.Id, spec.Email, db);
            }
        }

        Console.WriteLine("[seed-uat] Hoàn tất nạp dữ liệu UAT an toàn (Idempotent seed completed successfully).");
        return 0;
    }

    private static async Task EnsureDomainDataAsync(Guid userId, string email, FitmateDbContext db)
    {
        if (email == "uat.pt.verified@fitmate.local")
        {
            var hasProfile = await db.PtProfiles.AnyAsync(p => p.UserId == userId);
            if (!hasProfile)
            {
                var ptProfile = PtProfile.CreateDraft(userId, "Hypertrophy & Fat Loss", 5, 350000, "HLV thể hình có chứng chỉ quốc tế.");
                db.PtProfiles.Add(ptProfile);

                var cert = Certificate.Submit(userId, "NASM Certified Personal Trainer", "NASM", "NASM-CPT-8826", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)), null);
                cert.Approve(Guid.NewGuid());
                db.Certificates.Add(cert);

                var s1 = Service.Create(userId, "1-on-1 PT Gym Training", "Huấn luyện cá nhân chuyên sâu 60 phút", 350000, 60);
                var s2 = Service.Create(userId, "Nutrition Coaching", "Tư vấn chế độ ăn cá nhân hóa", 200000, 45);
                db.Services.AddRange(s1, s2);

                var now = DateTime.UtcNow;
                for (int d = 1; d <= 5; d++)
                {
                    var slotTime = now.Date.AddDays(d).AddHours(9);
                    var slot = AvailabilitySlot.Create(userId, slotTime, "Asia/Ho_Chi_Minh", 60);
                    db.AvailabilitySlots.Add(slot);
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"[seed-uat] Đã tạo PT Profile, Certificate (Approved), 2 Services và 5 Availability Slots cho '{email}'.");
            }
        }
        else if (email == "uat.pt.pending@fitmate.local")
        {
            var hasProfile = await db.PtProfiles.AnyAsync(p => p.UserId == userId);
            if (!hasProfile)
            {
                var ptProfile = PtProfile.CreateDraft(userId, "Calisthenics & Mobility", 2, 250000, "HLV đường phố đang nộp hồ sơ chứng nhận.");
                db.PtProfiles.Add(ptProfile);

                var cert = Certificate.Submit(userId, "ACE Fitness Instructor", "ACE", "ACE-PENDING-101", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)), null);
                db.Certificates.Add(cert);
                await db.SaveChangesAsync();
                Console.WriteLine($"[seed-uat] Đã tạo PT Profile và Certificate (Pending) cho '{email}'.");
            }
        }
        else if (email == "uat.member@fitmate.local")
        {
            var hasProfile = await db.UserProfiles.AnyAsync(p => p.UserId == userId);
            if (!hasProfile)
            {
                var profile = UserProfile.Create(userId);
                profile.Update(
                    phone: "0901234567",
                    gender: Gender.Female,
                    birthday: new DateOnly(1998, 5, 20),
                    heightCm: 165,
                    weightKg: 55.5m,
                    fitnessGoal: "Tăng cơ giảm mỡ",
                    trainingDays: new List<string> { "Monday", "Wednesday", "Friday" });
                db.UserProfiles.Add(profile);
            }

            var hasPref = await db.UserPreferences.AnyAsync(p => p.UserId == userId);
            if (!hasPref)
            {
                var pref = UserPreference.CreateDefault(userId);
                db.UserPreferences.Add(pref);
            }

            var hasPlan = await db.WorkoutPlans.AnyAsync(p => p.UserId == userId);
            if (!hasPlan)
            {
                var plan = WorkoutPlan.Create(userId, "Monday", "Kế hoạch tập phục hồi toàn thân", 45, 300);
                db.WorkoutPlans.Add(plan);
            }

            var hasMeal = await db.MealLogs.AnyAsync(m => m.UserId == userId);
            if (!hasMeal)
            {
                var meal = MealLog.Create(userId, "Ức gà áp chảo và cơm gạo lứt", null, 480, 42, 50, 10, 1.0m, MealSource.Manual, null, true, DateTime.UtcNow);
                db.MealLogs.Add(meal);
            }

            await db.SaveChangesAsync();
            Console.WriteLine($"[seed-uat] Đã hoàn tất Profile, Preferences, Workout Plan và Meal Log cho Member '{email}'.");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Sử dụng:
          dotnet run --project src/Fitmate.Api -- seed-uat-data [--allow-uat-seed]

        Chốt chặn an toàn:
          - Bắt buộc cờ '--allow-uat-seed' hoặc biến môi trường UAT_SEED_ENABLED=true.
          - Cấm thực thi trên môi trường Production (ASPNETCORE_ENVIRONMENT=Production).
          - Chỉ chấp nhận kết nối đến database UAT/Staging: 'fitmate_uat', 'fitmate_staging'
            hoặc danh sách khai báo trong biến ALLOWED_UAT_DB_NAMES.
          - Tuyệt đối KHÔNG truyền mật khẩu qua tham số dòng lệnh.
          - Nhập mật khẩu qua STDIN khi có prompt hoặc truyền biến môi trường bảo mật UAT_SEED_PASSWORD.
          - Chạy lại an toàn (Idempotent): Tài khoản và cấu hình MFA đã có sẽ được giữ nguyên vẹn.
        """);
    }

    private sealed record PersonaSpec(string Email, string FullName, string Role);
}
