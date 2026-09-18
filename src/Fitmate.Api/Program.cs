using Fitmate.Application.Common;
using Fitmate.Api.Cli;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// CLI dispatch: neu co args[0] = seed-admin (hoac tuong lai create-user, etc.) thi chay command roi thoat.
// Khong khoi dong web host -> nhanh, it footprint.
if (args.Length > 0)
{
    var config = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    int exitCode = args[0] switch
    {
        "seed-admin" => await SeedAdminCommand.RunAsync(args[1..], config),
        _ => UnknownCommand(args[0])
    };
    Environment.Exit(exitCode);
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"[fitmate-api] Unknown command: '{cmd}'. Supported: seed-admin");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

// Configuration: env var > appsettings.
builder.Configuration.AddEnvironmentVariables();

// Options binding.
builder.Services.Configure<AppOptions>(builder.Configuration);
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// DB.
var dbHost = builder.Configuration["DB_HOST"] ?? "localhost";
var dbPort = builder.Configuration["DB_PORT"] ?? "5432";
var dbName = builder.Configuration["DB_NAME"] ?? "fitmate";
var dbUser = builder.Configuration["DB_USER"] ?? "fitmate";
var dbPassword = builder.Configuration["DB_PASSWORD"] ?? throw new InvalidOperationException("DB_PASSWORD must be set");
var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

builder.Services.AddDbContext<FitmateDbContext>(opt =>
{
    opt.UseNpgsql(connectionString, npg =>
    {
        npg.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
    });
});

// Services.
builder.Services.AddSingleton<Fitmate.Application.Common.IPasswordHasher, Fitmate.Application.Common.BcryptPasswordHasher>();
builder.Services.AddSingleton<Fitmate.Application.Common.ITokenHasher, Fitmate.Application.Common.Sha256TokenHasher>();

// Controllers + ProblemDetails (RFC 7807).
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(opt =>
    {
        opt.InvalidModelStateResponseFactory = ctx =>
        {
            var problem = new ValidationProblemDetails(ctx.ModelState)
            {
                Status = 400,
                Title = "Validation failed",
                Type = "https://docs.fitmate.vn/errors/validation",
                Detail = "One or more fields failed validation."
            };
            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

// Health checks.
builder.Services.AddHealthChecks()
    .AddCheck<Fitmate.Infrastructure.Persistence.DatabaseHealthCheck>("database", tags: new[] { "ready" });

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var traceId = context.TraceIdentifier;
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://docs.fitmate.vn/errors/internal",
            title = "Internal server error",
            status = 500,
            detail = "Unexpected error. See server logs with traceId.",
            traceId,
            code = "FITMATE.GENERAL.INTERNAL"
        });
    });
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name == "database"
});

app.MapGet("/", () => Results.Ok(new
{
    name = "Fitmate API",
    version = "1.0.0",
    docs = "https://docs.fitmate.vn"
}));

app.MapControllers();

app.Run();
