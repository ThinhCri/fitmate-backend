using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitmate.Api.Cli;

public static class ReconcileAiOrdersCommand
{
    public static async Task<int> RunAsync(string[] ids, IConfiguration config)
    {
        if (ids.Length is < 1 or > 100 || ids.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x,
                @"\AGPA\.[0-9]{4}-[0-9]{4}-[0-9]{4}-[0-9]{5}(?:\.\.[0-9]+)?\z")))
        {
            Console.Error.WriteLine("Provide between 1 and 100 valid Google GPA order IDs.");
            return 1;
        }
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = config["DB_HOST"] ?? "localhost", Port = int.Parse(config["DB_PORT"] ?? "5432"),
            Database = config["DB_NAME"] ?? "fitmate", Username = config["DB_USER"] ?? "fitmate",
            Password = config["DB_PASSWORD"] ?? throw new InvalidOperationException("DB_PASSWORD is required.")
        };
        var options = Options.Create(config.GetSection("GooglePlayBilling").Get<GooglePlayBillingOptions>() ?? new());
        var protection = DataProtectionProvider.Create(new DirectoryInfo(config["DATA_PROTECTION_KEYS_PATH"]
            ?? "/home/fitmate/.aspnet/DataProtection-Keys"), b => b.SetApplicationName("Fitmate.Api"));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var google = new GooglePlaySubscriptionVerifier(http, options);
        var failures = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            // A fresh context prevents one rejected order from poisoning the next recovery.
            await using var db = new FitmateDbContext(new DbContextOptionsBuilder<FitmateDbContext>()
                .UseNpgsql(connection.ConnectionString, x => x.EnableRetryOnFailure(3)).Options);
            var subscriptions = new EfAiSubscriptionService(db, google, options, protection);
            var sync = new AiPlayOrderSync(db, subscriptions, google, protection);
            try
            {
                await new GooglePlayOrderRecovery(db, google, google, subscriptions, sync).RecoverAsync(id, default);
                Console.WriteLine($"ORDER_RECONCILED {id}");
            }
            catch (DomainException ex)
            {
                failures++;
                Console.Error.WriteLine($"ORDER_NOT_RECONCILED {id} CODE={ex.Code}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"ORDER_NOT_RECONCILED {id} TYPE={ex.GetType().Name}");
            }
        }
        Console.WriteLine($"AI_ORDER_RECONCILIATION_COMPLETED FAILED={failures}");
        return failures == 0 ? 0 : 2;
    }
}
