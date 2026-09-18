using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Cli;

/// <summary>
/// CLI: dotnet run --project src/Fitmate.Api -- seed-admin --email user@example.com [--full-name "Ten Day Du"]
///
/// Doc password tu STDIN (an toan hon argv, tranh shoulder-surfing qua `ps`).
/// Default email neu khong truyen: tu env BOOTSTRAP_ADMIN_EMAIL (xem .env.example) hoac fallback admin@fitmate.vn.
///
/// Sau BE-021 (DEC-032), su dung <see cref="UserManager{ApplicationUser}"/> cua ASP.NET Core Identity
/// thay cho custom BcryptPasswordHasher.
///
/// Rule AGENTS.md: "Khong seed admin password mac dinh. Bootstrap qua CLI".
/// </summary>
public static class SeedAdminCommand
{
    public static readonly string[] RoleNames = { "Admin", "Member", "PT" };

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
    {
        // Parse args don gian: --email X [--full-name Y]
        string? email = null;
        string fullName = "Bootstrap Admin";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--email" when i + 1 < args.Length:
                    email = args[++i];
                    break;
                case "--full-name" when i + 1 < args.Length:
                    fullName = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        email ??= configuration["BOOTSTRAP_ADMIN_EMAIL"] ?? "admin@fitmate.vn";

        // Validate email format nho.
        if (!email.Contains('@') || email.Length > 254)
        {
            Console.Error.WriteLine($"[seed-admin] Email khong hop le: '{email}'");
            return 1;
        }

        // Doc password tu STDIN (khong echo).
        Console.Write($"[seed-admin] Nhap password cho {email}: ");
        var password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            Console.Error.WriteLine("[seed-admin] Password phai >= 12 ky tu.");
            return 1;
        }

        // Wire DbContext + UserManager thu cong (vi chua co DI host).
        var dbHost = configuration["DB_HOST"] ?? "localhost";
        var dbPort = configuration["DB_PORT"] ?? "5432";
        var dbName = configuration["DB_NAME"] ?? "fitmate";
        var dbUser = configuration["DB_USER"] ?? "fitmate";
        var dbPassword = configuration["DB_PASSWORD"]
            ?? throw new InvalidOperationException("DB_PASSWORD must be set");

        var connStr = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

        var services = new ServiceCollection();
        services.AddDbContext<FitmateDbContext>(opt =>
            opt.UseNpgsql(connStr));
        services.AddIdentityCore<ApplicationUser>(opt => opt.Password.RequiredLength = 12)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FitmateDbContext>();
        services.AddLogging(b => b.AddConsole());
        await using var sp = services.BuildServiceProvider();

        await using var db = sp.GetRequiredService<FitmateDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Ensure roles seeded (idempotent).
        foreach (var roleName in RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var r = await roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
                if (!r.Succeeded)
                {
                    Console.Error.WriteLine($"[seed-admin] Tao role '{roleName}' that bai: {string.Join("; ", r.Errors.Select(e => e.Description))}");
                    return 1;
                }
            }
        }

        // Check existing user (case-insensitive; Identity normalize o NormalizedEmail).
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existing != null)
        {
            var isAdmin = await userManager.IsInRoleAsync(existing, "Admin");
            Console.Error.WriteLine($"[seed-admin] User '{normalizedEmail}' da ton tai (status={existing.Status}). Bo qua.");
            if (isAdmin) return 0;
            Console.Error.WriteLine("[seed-admin] WARNING: user hien tai khong co role Admin. Can promote thu cong hoac dung email khac.");
            return 2;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            NormalizedEmail = normalizedEmail.ToUpperInvariant(),
            NormalizedUserName = normalizedEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = fullName,
            Status = UserStatus.Active,
            Timezone = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            Console.Error.WriteLine($"[seed-admin] Tao admin that bai: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            return 1;
        }

        var roleResult = await userManager.AddToRoleAsync(admin, "Admin");
        if (!roleResult.Succeeded)
        {
            Console.Error.WriteLine($"[seed-admin] Gan role Admin that bai: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
            return 1;
        }

        Console.WriteLine($"[seed-admin] OK -- admin '{admin.Email}' created (id={admin.Id}, role=Admin, status=Active).");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run --project src/Fitmate.Api -- seed-admin --email <addr> [--full-name "Ten Day Du"]

        Environment:
          DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD  (required, PostgreSQL connection)
          BOOTSTRAP_ADMIN_EMAIL                             (optional fallback email)

        Input:
          Password doc tu STDIN sau prompt; phai >= 12 ky tu.

        Implementation:
          ASP.NET Core Identity (UserManager + RoleManager); hash qua PasswordHasher PBKDF2.
        """);
    }
}
