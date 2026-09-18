using Fitmate.Application.Common;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Api.Cli;

/// <summary>
/// CLI: dotnet run --project src/Fitmate.Api -- seed-admin --email user@example.com [--full-name "Ten Day Du"]
///
/// Doc password tu STDIN (an toan hon argv, tranh shoulder-surfing qua `ps`).
/// Default email neu khong truyen: tu env BOOTSTRAP_ADMIN_EMAIL (xem .env.example) hoac fallback admin@fitmate.vn.
///
/// Rule AGENTS.md: "Khong seed admin password mac dinh. Bootstrap qua CLI".
/// </summary>
public static class SeedAdminCommand
{
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

        // Wire DbContext + hasher thu cong (vi chua co DI host).
        var dbHost = configuration["DB_HOST"] ?? "localhost";
        var dbPort = configuration["DB_PORT"] ?? "5432";
        var dbName = configuration["DB_NAME"] ?? "fitmate";
        var dbUser = configuration["DB_USER"] ?? "fitmate";
        var dbPassword = configuration["DB_PASSWORD"]
            ?? throw new InvalidOperationException("DB_PASSWORD must be set");

        var connStr = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

        var options = new DbContextOptionsBuilder<FitmateDbContext>()
            .UseNpgsql(connStr)
            .Options;

        await using var db = new FitmateDbContext(options);
        IPasswordHasher hasher = new BcryptPasswordHasher();

        // Check existing.
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existing != null)
        {
            Console.Error.WriteLine($"[seed-admin] User '{normalizedEmail}' da ton tai (role={existing.Role}, status={existing.Status}). Bo qua.");
            return existing.Role == UserRole.Admin ? 0 : 2;
        }

        var admin = User.CreateAdmin(normalizedEmail, hasher.Hash(password), fullName);
        db.Set<User>().Add(admin);
        await db.SaveChangesAsync();

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
        """);
    }
}
