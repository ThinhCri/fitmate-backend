using System.Net.Http.Headers;
using Fitmate.Application.Auth;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Fitmate.IntegrationTests.Infrastructure;

/// <summary>
/// BE-820: WebApplicationFactory test harness backed by real isolated PostgreSQL database.
/// Strictly avoids EF InMemory to ensure DB constraints, triggers, and transactions are tested.
/// </summary>
public class FitmateTestAppFactory : WebApplicationFactory<Program>
{
    public static string DbHost => Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
    public static string DbPort => Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
    public static string DbUser => Environment.GetEnvironmentVariable("DB_USER") ?? "fitmate";
    // One fresh database per test process, shared by all factory instances in that process.
    public static string TestDbName { get; } = ResolveTestDatabaseName();

    private static string ResolveTestDatabaseName()
    {
        var name = Environment.GetEnvironmentVariable("TEST_DB_NAME");
        if (string.IsNullOrWhiteSpace(name))
            return "fitmate_integration_test_" + Guid.NewGuid().ToString("N");

        if (name.Length > 63 ||
            !(name.StartsWith("fitmate_integration_test", StringComparison.Ordinal) ||
              name.StartsWith("fitmate_payos_it_", StringComparison.Ordinal)) ||
            name.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new InvalidOperationException(
                "TEST_DB_NAME must be an isolated test database: fitmate_integration_test* or fitmate_payos_it_*, " +
                "using lowercase ASCII letters, digits and underscores (maximum 63 characters).");

        return name;
    }

    public static string DbPassword { get; private set; } = "dev";

    static FitmateTestAppFactory()
    {
        DbPassword = ResolveWorkingPassword();

        Environment.SetEnvironmentVariable("DB_HOST", DbHost);
        Environment.SetEnvironmentVariable("DB_PORT", DbPort);
        Environment.SetEnvironmentVariable("DB_NAME", TestDbName);
        Environment.SetEnvironmentVariable("DB_USER", DbUser);
        Environment.SetEnvironmentVariable("DB_PASSWORD", DbPassword);
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", "dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3Q=");

        EnsureTestDatabaseAndSchemaCreated(DbPassword);
    }

    private static string ResolveWorkingPassword()
    {
        var explicitPw = Environment.GetEnvironmentVariable("DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(explicitPw)) return explicitPw;

        // Try 'dev' (local container port 5432) then 'localverify300pw'
        string[] candidates = { "dev", "localverify300pw" };
        foreach (var pw in candidates)
        {
            try
            {
                using var conn = new NpgsqlConnection($"Host={DbHost};Port={DbPort};Database=fitmate;Username={DbUser};Password={pw}");
                conn.Open();
                return pw;
            }
            catch (PostgresException ex) when (ex.SqlState == "28P01")
            {
                // Password incorrect, try next
            }
            catch
            {
                // Server might be listening on postgres db instead
                try
                {
                    using var conn2 = new NpgsqlConnection($"Host={DbHost};Port={DbPort};Database=postgres;Username={DbUser};Password={pw}");
                    conn2.Open();
                    return pw;
                }
                catch
                {
                    // Continue
                }
            }
        }
        return "dev";
    }

    private static void EnsureTestDatabaseAndSchemaCreated(string password)
    {
        // 1. Ensure test database exists on PostgreSQL
        string[] adminDbs = { "postgres", "fitmate" };
        var databaseAvailable = false;
        foreach (var adminDb in adminDbs)
        {
            try
            {
                using var conn = new NpgsqlConnection($"Host={DbHost};Port={DbPort};Database={adminDb};Username={DbUser};Password={password}");
                conn.Open();

                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
                checkCmd.Parameters.AddWithValue("name", TestDbName);
                var exists = checkCmd.ExecuteScalar();
                if (exists == null)
                {
                    using var createCmd = conn.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE \"{TestDbName}\"";
                    createCmd.ExecuteNonQuery();
                }
                databaseAvailable = true;
                break;
            }
            catch
            {
                // Continue to next admin DB
            }
        }

        if (!databaseAvailable)
            throw new InvalidOperationException(
                "Cannot provision the isolated integration database. Check DB_HOST/DB_PORT/DB_USER/DB_PASSWORD " +
                "and CREATEDB permission, or supply an empty test database through TEST_DB_NAME.");

        // EnsureCreated tests the current model, not the migration chain. Never reuse a stale schema.
        // Leave the test database intact for diagnosis; do not delete user databases or volumes.
        {
            var optionsBuilder = new DbContextOptionsBuilder<FitmateDbContext>();
            optionsBuilder.UseNpgsql($"Host={DbHost};Port={DbPort};Database={TestDbName};Username={DbUser};Password={password}");
            using var db = new FitmateDbContext(optionsBuilder.Options);
            if (!db.Database.EnsureCreated())
                throw new InvalidOperationException(
                    "The integration database already contains tables. EnsureCreated cannot upgrade its schema. " +
                    "Unset TEST_DB_NAME for a fresh per-run database, or select a new empty test database. " +
                    "Existing data has not been deleted.");
            Console.WriteLine($"[FitmateTestAppFactory] Isolated test database: {TestDbName}");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_HOST"] = DbHost,
                ["DB_PORT"] = DbPort,
                ["DB_NAME"] = TestDbName,
                ["DB_USER"] = DbUser,
                ["DB_PASSWORD"] = DbPassword,
                ["JWT_SIGNING_KEY"] = "dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3Q=",
                ["Ai:Enabled"] = "false",
                ["Ai:FoodScanEnabled"] = "false",
                ["Ai:CompanionChatEnabled"] = "false",
                ["Ai:WorkoutAdjustEnabled"] = "false",
                ["Storage:BasePath"] = Path.Combine(Path.GetTempPath(), "fitmate-integration-uploads"),
                ["Payment:AllowMockProvider"] = "true",
                ["Vnpay:TmnCode"] = "FITMATETEST",
                ["Vnpay:HashSecret"] = "VNPAYINTEGRATIONTESTSECRETKEY1234567890ABCDEF1234567890ABCDEF",
                ["Vnpay:PaymentUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["Vnpay:ApiUrl"] = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction",
                ["Vnpay:ReturnUrl"] = "http://localhost:5000/api/v1/payments/vnpay/return"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Windows test compatibility: persist DataProtection keys to temp directory
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "fitmate-test-dpkeys")))
                .SetApplicationName("Fitmate.Api");
        });
    }

    public void EnsureDatabaseInitialized()
    {
        // Schema is initialized in static constructor before host boots
    }

    public HttpClient CreateAuthenticatedClient(string role = "Member", Guid? userId = null)
    {
        var client = CreateClient();
        var uid = userId ?? Guid.NewGuid();

        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.Issue(new JwtTokenRequest(
            uid,
            $"{role.ToLowerInvariant()}_{uid:N}@fitmate.vn",
            role,
            $"Test {role}"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
