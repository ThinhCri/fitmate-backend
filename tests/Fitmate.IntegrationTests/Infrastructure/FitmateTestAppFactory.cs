using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        builder.ConfigureLogging(logging =>
        {
            // WebApplicationFactory must not inherit the Windows Event Log provider:
            // non-elevated test runners cannot write to the .NET Runtime event source.
            logging.ClearProviders();
            logging.AddConsole();
        });

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
                ["Payment:AllowMockProvider"] = "false",
                ["Vnpay:TmnCode"] = "FITMATETEST",
                ["Vnpay:HashSecret"] = "VNPAYINTEGRATIONTESTSECRETKEY1234567890ABCDEF1234567890ABCDEF",
                ["Vnpay:PaymentUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["Vnpay:ApiUrl"] = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction",
                ["Vnpay:ReturnUrl"] = "http://localhost:5000/api/v1/payments/vnpay/return"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddScoped<Fitmate.Application.Payments.IPaymentProvider, Fitmate.Application.Payments.StubPaymentProvider>();
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

    public HttpClient CreateAuthenticatedClient(
        string role = "Member",
        Guid? userId = null,
        bool includeFinanceTotp = false)
    {
        var client = CreateClient();
        var uid = userId ?? Guid.NewGuid();

        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = users.FindByIdAsync(uid.ToString()).GetAwaiter().GetResult();
        if (user is null)
        {
            var email = $"{role.ToLowerInvariant()}_{uid:N}@fitmate.vn";
            user = new ApplicationUser
            {
                Id = uid,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = $"Test {role}",
                Status = UserStatus.Active,
                Timezone = "Asia/Ho_Chi_Minh",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createResult = users.CreateAsync(user).GetAwaiter().GetResult();
            if (!createResult.Succeeded)
                throw new InvalidOperationException(
                    $"Unable to create integration-test user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            var stampResult = users.UpdateSecurityStampAsync(user).GetAwaiter().GetResult();
            if (!stampResult.Succeeded)
                throw new InvalidOperationException("Unable to initialize the integration-test user's security stamp.");
        }

        if (!users.IsInRoleAsync(user, role).GetAwaiter().GetResult())
        {
            var roleResult = users.AddToRoleAsync(user, role).GetAwaiter().GetResult();
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Unable to assign integration-test role: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
        }

        var requiresMfa = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        string? financeTotp = null;
        if (requiresMfa && includeFinanceTotp)
        {
            var resetResult = users.ResetAuthenticatorKeyAsync(user).GetAwaiter().GetResult();
            if (!resetResult.Succeeded)
                throw new InvalidOperationException("Unable to initialize the integration-test admin authenticator key.");
        }

        if (requiresMfa && !user.TwoFactorEnabled)
        {
            var mfaResult = users.SetTwoFactorEnabledAsync(user, true).GetAwaiter().GetResult();
            if (!mfaResult.Succeeded)
                throw new InvalidOperationException("Unable to enable MFA for the integration-test admin.");
        }

        if (requiresMfa && includeFinanceTotp)
        {
            var authenticatorKey = users.GetAuthenticatorKeyAsync(user).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(authenticatorKey))
                throw new InvalidOperationException("Unable to read the integration-test admin authenticator key.");
            financeTotp = GenerateTotp(authenticatorKey, DateTimeOffset.UtcNow);
        }

        var sessions = scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
        var authenticationMethod = requiresMfa ? "mfa" : "pwd";
        var session = sessions.CreateAsync(
            user.Id,
            user.SecurityStamp!,
            requiresMfa,
            CancellationToken.None).GetAwaiter().GetResult();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.Issue(new JwtTokenRequest(
            user.Id,
            user.Email!,
            role,
            user.FullName,
            user.SecurityStamp,
            authenticationMethod,
            SessionId: session.Id));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (financeTotp is not null)
            client.DefaultRequestHeaders.Add("X-Finance-Totp", financeTotp);
        return client;
    }

    private static string GenerateTotp(string base32Key, DateTimeOffset timestamp)
    {
        var key = DecodeBase32(base32Key);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, timestamp.ToUnixTimeSeconds() / 30);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>((value.Length * 5 + 7) / 8);
        var buffer = 0;
        var bitsInBuffer = 0;

        foreach (var raw in value)
        {
            if (raw is ' ' or '-' or '=') continue;
            var index = alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0) throw new FormatException("Authenticator key is not valid Base32.");
            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8) continue;
            bitsInBuffer -= 8;
            output.Add((byte)(buffer >> bitsInBuffer));
            buffer &= (1 << bitsInBuffer) - 1;
        }

        return output.ToArray();
    }
}
