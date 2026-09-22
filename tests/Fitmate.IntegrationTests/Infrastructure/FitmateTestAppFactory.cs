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
    public static string TestDbName => Environment.GetEnvironmentVariable("TEST_DB_NAME") ?? "fitmate_integration_test";

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
        string[] adminDbs = { "fitmate", "postgres" };
        foreach (var adminDb in adminDbs)
        {
            try
            {
                using var conn = new NpgsqlConnection($"Host={DbHost};Port={DbPort};Database={adminDb};Username={DbUser};Password={password}");
                conn.Open();

                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{TestDbName}'";
                var exists = checkCmd.ExecuteScalar();
                if (exists == null)
                {
                    using var createCmd = conn.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE {TestDbName}";
                    createCmd.ExecuteNonQuery();
                }
                break;
            }
            catch
            {
                // Continue to next admin DB
            }
        }

        // 2. Early schema creation: Ensure all tables and constraints exist before WebApplicationFactory boots Program.cs
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<FitmateDbContext>();
            optionsBuilder.UseNpgsql($"Host={DbHost};Port={DbPort};Database={TestDbName};Username={DbUser};Password={password}");
            using var db = new FitmateDbContext(optionsBuilder.Options);
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FitmateTestAppFactory] Warning: Early EnsureCreated encountered: {ex.Message}");
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
                ["Payment:AllowMockProvider"] = "true"
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
