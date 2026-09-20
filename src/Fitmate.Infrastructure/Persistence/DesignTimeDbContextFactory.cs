using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Fitmate.Infrastructure.Persistence;

/// <summary>
/// Design-time factory để <c>dotnet ef migrations</c> không cần chạy qua Program.cs.
/// Program.cs dùng JwtOptionsLoader (BE-380) để load JWT key; factory này đọc DB config từ env (hoặc dùng localhost/default cho local dev).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FitmateDbContext>
{
    public FitmateDbContext CreateDbContext(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var db = Environment.GetEnvironmentVariable("DB_NAME") ?? "fitmate";
        var user = Environment.GetEnvironmentVariable("DB_USER") ?? "fitmate";
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "dev";

        var connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={password}";

        var optionsBuilder = new DbContextOptionsBuilder<FitmateDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new FitmateDbContext(optionsBuilder.Options);
    }
}
