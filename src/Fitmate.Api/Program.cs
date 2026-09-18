using Fitmate.Application.Common;
using Fitmate.Api.Cli;
using Fitmate.Api.Errors;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// CLI dispatch: neu co args[0] = seed-admin (hoac tuong lai create-user, etc.) thi chay command roi thoat.
// Khong khoi dong web host -> nhanh, it footprint.
// args[0] co the la flag cua dotnet/ef tool (--applicationName, --project, --verbosity, ...)
// thi KHONG phai command; chi dispatch khi args[0] KHONG bat dau bang '--'.
if (args.Length > 0 && !args[0].StartsWith("--"))
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

// Identity (DEC-032). BE-021: AddIdentityCore + Roles + EF stores.
// Dung AddIdentityCore thay vi AddIdentity de khong ep cookie auth; API chi dung JWT bearer.
// AddDataProtection cung cap IDataProtectionProvider cho AddDefaultTokenProviders
// (reset password + email confirm token).
builder.Services.AddDataProtection();
builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.Password.RequiredLength = 12;
    opt.Password.RequireDigit = true;
    opt.Password.RequireUppercase = false;
    opt.Password.RequireLowercase = false;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Lockout.AllowedForNewUsers = true;
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<FitmateDbContext>()
.AddDefaultTokenProviders(); // reset password + email confirm token

// Services.
builder.Services.AddSingleton<Fitmate.Application.Common.IPasswordHasher, Fitmate.Application.Common.BcryptPasswordHasher>();
builder.Services.AddSingleton<Fitmate.Application.Common.ITokenHasher, Fitmate.Application.Common.Sha256TokenHasher>();

// Controllers + ProblemDetails (RFC 7807). BE-080: format chuẩn hoá qua ProblemDetailsFactory
// (code + traceId theo api-contract.md §6).
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(opt =>
    {
        opt.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState
                .Where(kv => kv.Value is not null && kv.Value.Errors.Count > 0)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            var problem = ProblemDetailsFactory.Create(
                status: 400,
                title: "Validation failed",
                code: ErrorCodes.ValidationFailed,
                typeSlug: "validation",
                detail: "One or more fields failed validation.",
                traceId: ProblemDetailsFactory.ResolveTraceId(ctx.HttpContext),
                errors: errors);

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
        var traceId = ProblemDetailsFactory.ResolveTraceId(context);

        // BE-080: mọi 4xx/5xx đi qua factory để format đồng nhất (code + traceId + type).
        // BE-060+ sẽ map exception cụ thể (vd SlotTakenException -> 409) sang code tương ứng.
        var (status, title, code, typeSlug) = ex switch
        {
            ArgumentException => (400, "Bad request", ErrorCodes.GeneralBadRequest, "bad-request"),
            KeyNotFoundException => (404, "Resource not found", ErrorCodes.GeneralNotFound, "not-found"),
            UnauthorizedAccessException => (401, "Unauthorized", ErrorCodes.AuthUnauthorized, "unauthorized"),
            _ => (500, "Internal server error", ErrorCodes.GeneralInternal, "internal")
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = ProblemDetailsFactory.Create(
            status: status,
            title: title,
            code: code,
            typeSlug: typeSlug,
            detail: status == 500
                ? "Unexpected error. See server logs with traceId."
                : ex?.Message,
            traceId: traceId);

        await context.Response.WriteAsJsonAsync(problem);
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
