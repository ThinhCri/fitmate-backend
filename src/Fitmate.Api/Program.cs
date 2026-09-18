using Fitmate.Application.Common;
using Fitmate.Api.Cli;
using Fitmate.Api.Errors;
using Fitmate.Api.Idempotency;
using Fitmate.Api.Outbox;
using Fitmate.Application.Idempotency;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using FluentValidation;
using Fitmate.Application.Auth;
using Fitmate.Application.Auth.Validators;
using Fitmate.Application.Auth.Contracts;

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

// BE-030: Auth services.
builder.Services.AddSingleton<IJwtTokenService>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>().Value.Jwt;
    return new JwtTokenService(options);
});
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<Fitmate.Application.Auth.IRefreshTokenStore, Fitmate.Infrastructure.Auth.EfRefreshTokenStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Fitmate.Api.Auth.ICurrentUserAccessor, Fitmate.Api.Auth.HttpContextCurrentUserAccessor>();

// BE-030: FluentValidation auto-validate cho Controller.
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();

// BE-070: Idempotency (DEC-011) + Outbox (DEC-012) infrastructure.
// - IIdempotencyStore: EF Core backed.
// - IIdempotencyService: dùng store + TTL 24h mặc định (api-contract.md §11).
// - IOutboxDispatcher: enqueue message vào DbContext (cùng transaction use case).
// - IOutboxStore: EF Core backed, worker dùng để fetch due + purge.
// - OutboxWorker: pure service, xử lý batch + retry với backoff.
// - OutboxBackgroundService: hosted service poll worker theo interval.
// IOutboxHandler implementations đăng ký ở các module nghiệp vụ (booking, payment, ...).
builder.Services.AddScoped<IIdempotencyStore, Fitmate.Infrastructure.Idempotency.EfIdempotencyStore>();
builder.Services.AddScoped<IIdempotencyService>(sp => new IdempotencyService(
    sp.GetRequiredService<IIdempotencyStore>(),
    ttl: TimeSpan.FromHours(24)));
builder.Services.AddScoped<IOutboxQueue, Fitmate.Infrastructure.Outbox.EfOutboxQueue>();
builder.Services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
builder.Services.AddScoped<IOutboxStore, Fitmate.Infrastructure.Outbox.EfOutboxStore>();
builder.Services.AddScoped<OutboxWorker>(sp => new OutboxWorker(
    sp.GetRequiredService<IOutboxStore>(),
    sp.GetServices<IOutboxHandler>(),
    sp.GetRequiredService<ILogger<OutboxWorker>>()));
builder.Services.AddHostedService<OutboxBackgroundService>();

// BE-030: JWT Bearer authentication.
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException("JwtOptions.SigningKey must be configured (env JWT_SIGNING_KEY)");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// Controllers + ProblemDetails (RFC 7807). BE-080: format chuẩn hoá qua FitmateProblemDetails
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

            var problem = FitmateProblemDetails.Create(
                status: 400,
                title: "Validation failed",
                code: ErrorCodes.ValidationFailed,
                typeSlug: "validation",
                detail: "One or more fields failed validation.",
                traceId: FitmateProblemDetails.ResolveTraceId(ctx.HttpContext),
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

// BE-030: JWT auth pipeline.
app.UseAuthentication();
app.UseAuthorization();

// BE-070: Idempotency middleware (DEC-011). Áp dụng cho mọi mutation có Idempotency-Key header.
// Đặt SAU authentication để chỉ nhận key đã qua auth filter, nhưng TRƯỚC MapControllers
// để có thể short-circuit khi replay/conflict.
app.UseMiddleware<IdempotencyMiddleware>();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var traceId = FitmateProblemDetails.ResolveTraceId(context);

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

        var problem = FitmateProblemDetails.Create(
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
