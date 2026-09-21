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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using FluentValidation;
using Fitmate.Application.Auth;
using Fitmate.Application.Auth.Validators;
using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Trainers;
using Fitmate.Application.Booking;
using Fitmate.Application.Identity;
using Fitmate.Application.Finance;
using Fitmate.Application.Admin;
using Fitmate.Application.Social;
using Fitmate.Application.Chat;
using Fitmate.Application.Moderation;
using Fitmate.Application.Notifications;
using Fitmate.Application.Notifications.Handlers;
using Fitmate.Application.Workout;
using Fitmate.Application.Nutrition;

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
builder.Services.Configure<Fitmate.Application.Common.PlatformPolicyOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Common.PlatformPolicyOptions.SectionName));

// BE-380: load + validate JwtOptions tại một nơi duy nhất, hỗ trợ alias env
// (JWT_SIGNING_KEY canonical, Jwt__SigningKey theo ASP.NET Core convention, hoặc Jwt:SigningKey section).
// Fail-fast khi thiếu key, key quá ngắn, hoặc hai env alias xung đột giá trị.
// Register raw instance để RefreshTokenService (inject JwtOptions trực tiếp) resolve được;
// JwtTokenService + Bearer middleware cùng dùng instance này để đảm bảo một nguồn sự thật.
var jwtOptions = Fitmate.Application.Common.JwtOptionsLoader.Load(builder.Configuration);
builder.Services.AddSingleton(jwtOptions);

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
// BE-300: persist key ring to a named volume so push-token encryption survives container recreates.
// Production should swap to secret manager / certificate-backed keys (BE-340).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/home/fitmate/.aspnet/DataProtection-Keys"))
    .SetApplicationName("Fitmate.Api");
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
// BE-380: JwtTokenService + RefreshTokenService cùng resolve JwtOptions instance đã load tại startup
// (không qua IOptions<>) để đảm bảo một nguồn sự thật — JwtOptionsLoader.Load đã fail-fast ở đầu startup
// nên không thể có instance null ở đây.
builder.Services.AddSingleton<IJwtTokenService>(_ => new JwtTokenService(jwtOptions));
builder.Services.AddScoped<IRefreshTokenService>(sp => new RefreshTokenService(
    sp.GetRequiredService<Fitmate.Application.Auth.IRefreshTokenStore>(),
    sp.GetRequiredService<Fitmate.Application.Common.ITokenHasher>(),
    jwtOptions));
builder.Services.AddScoped<Fitmate.Application.Auth.IRefreshTokenStore, Fitmate.Infrastructure.Auth.EfRefreshTokenStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Fitmate.Api.Auth.ICurrentUserAccessor, Fitmate.Api.Auth.HttpContextCurrentUserAccessor>();

// BE-030: FluentValidation auto-validate cho Controller.
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();

// BE-040: Trainer + Certificate services.
builder.Services.AddScoped<IPtProfileStore, Fitmate.Infrastructure.Trainers.EfPtProfileStore>();
builder.Services.AddScoped<ICertificateStore, Fitmate.Infrastructure.Trainers.EfCertificateStore>();
builder.Services.AddScoped<IUserRoleLookup, Fitmate.Infrastructure.Trainers.IdentityUserRoleLookup>();
builder.Services.AddScoped<IPtProfileService, Fitmate.Application.Trainers.PtProfileService>();
builder.Services.AddScoped<ICertificateService, Fitmate.Application.Trainers.CertificateService>();
builder.Services.AddScoped<IAuditLogger, Fitmate.Infrastructure.Audit.EfAuditLogger>();

// BE-050: Service offering + Availability services.
builder.Services.AddScoped<IServiceOfferingStore, Fitmate.Infrastructure.Trainers.EfServiceOfferingStore>();
builder.Services.AddScoped<IAvailabilitySlotStore, Fitmate.Infrastructure.Trainers.EfAvailabilitySlotStore>();
builder.Services.AddScoped<IServiceOfferingService, Fitmate.Application.Trainers.ServiceOfferingService>();
builder.Services.AddScoped<IAvailabilityService, Fitmate.Application.Trainers.AvailabilityService>();

// BE-120: Trainer marketplace.
builder.Services.AddScoped<ITrainerMarketplaceStore, Fitmate.Infrastructure.Trainers.EfTrainerMarketplaceStore>();
builder.Services.AddScoped<ITrainerMarketplaceService, Fitmate.Application.Trainers.TrainerMarketplaceService>();

// BE-060: Booking lifecycle services.
builder.Services.AddScoped<IAppointmentStore, Fitmate.Infrastructure.Booking.EfAppointmentStore>();
builder.Services.AddScoped<IBookingHoldStore, Fitmate.Infrastructure.Booking.EfBookingHoldStore>();
builder.Services.AddScoped<IServiceBookingLookup, Fitmate.Infrastructure.Booking.ServiceBookingLookup>();
builder.Services.AddScoped<IBookingService, Fitmate.Application.Booking.BookingService>();

// BE-090: Authorization guard.
builder.Services.AddScoped<IAuthorizationGuard, Fitmate.Application.Auth.AuthorizationGuard>();

// BE-110: Admin user management.
builder.Services.AddScoped<IAdminUserStore, Fitmate.Infrastructure.Identity.EfAdminUserStore>();
builder.Services.AddScoped<IAdminUserService, Fitmate.Application.Identity.AdminUserService>();

// BE-150: Admin finance.
builder.Services.AddScoped<IAdminFinanceStore, Fitmate.Infrastructure.Finance.EfAdminFinanceStore>();
builder.Services.AddScoped<IAdminFinanceService, Fitmate.Application.Finance.AdminFinanceService>();

// BE-151: Platform policy (read-only).
builder.Services.AddScoped<IPlatformPolicyService, Fitmate.Application.Finance.PlatformPolicyService>();

// BE-220: Admin metrics + action-required.
builder.Services.AddScoped<IAdminMetricsStore, Fitmate.Infrastructure.Admin.EfAdminMetricsStore>();
builder.Services.AddScoped<IAdminMetricsService, Fitmate.Application.Admin.AdminMetricsService>();

// BE-180: Social (post + comment + reaction + report).
builder.Services.AddScoped<ISocialStore, Fitmate.Infrastructure.Social.EfSocialStore>();
builder.Services.AddScoped<ISocialService, Fitmate.Application.Social.SocialService>();

// BE-181: Friend + Block (request/accept/reject/cancel/unfriend + block/unblock).
builder.Services.AddScoped<IFriendStore, Fitmate.Infrastructure.Social.EfFriendStore>();
builder.Services.AddScoped<IBlockStore, Fitmate.Infrastructure.Social.EfBlockStore>();
builder.Services.AddScoped<IFriendService, Fitmate.Application.Social.FriendService>();
builder.Services.AddScoped<IBlockService, Fitmate.Application.Social.BlockService>();

// BE-191: friend chat (5 endpoints, friend scope only — AI chat is out of scope).
builder.Services.AddScoped<IChatStore, Fitmate.Infrastructure.Persistence.Chat.EfChatStore>();
builder.Services.AddScoped<IChatService, Fitmate.Application.Chat.ChatService>();

// BE-210+BE-211: Moderation (admin list/resolve/dismiss reports + warn/lock/delete).
builder.Services.AddScoped<IModerationStore, Fitmate.Infrastructure.Moderation.EfModerationStore>();
builder.Services.AddScoped<IModerationActionStore, Fitmate.Infrastructure.Moderation.EfModerationActionStore>();
builder.Services.AddScoped<IModerationService, Fitmate.Application.Moderation.ModerationService>();

// BE-200: Notifications — in-app inbox + push delivery (FCM/Expo on the wire).
// Push dispatcher commit strategy: each device row + notification row are saved in
// the same SaveChanges as the originating FriendService.SaveChangesAsync / ModerationService.WarnAsync.
// On startup the worker and sweep background service check `PushProviderConfig.Enabled` —
// when disabled they no-op cleanly so no deferred backlog accumulates.
// DataProtection: .NET 8 sets the application discriminator from the hosting
// content root so the same key ring can be shared across multiple instances of
// the same app. Production deployments should override the key ring path via
// PersistKeysToFileSystem / secret manager (see UA-DP-001).
builder.Services.AddDataProtection();
builder.Services.AddScoped<IPushDeliveryTokens, Fitmate.Infrastructure.Notifications.DataProtectionTokenCipher>();
builder.Services.AddScoped<INotificationStore, Fitmate.Infrastructure.Notifications.EfNotificationStore>();
builder.Services.AddScoped<IDeviceStore, Fitmate.Infrastructure.Notifications.EfDeviceStore>();
builder.Services.AddScoped<IPushStore, Fitmate.Infrastructure.Notifications.EfPushStore>();
builder.Services.AddScoped<INotificationService, Fitmate.Application.Notifications.NotificationService>();
builder.Services.AddScoped<IDeviceService, Fitmate.Application.Notifications.DeviceService>();
builder.Services.AddScoped<INotificationAuditLogger, Fitmate.Infrastructure.Notifications.NotificationAuditLogger>();
// BE-160: workout session + weight log.
builder.Services.AddScoped<IWorkoutStore, Fitmate.Infrastructure.Workout.EfWorkoutStore>();
builder.Services.AddScoped<IWeightStore, Fitmate.Infrastructure.Workout.EfWeightStore>();
builder.Services.AddScoped<IWorkoutService, Fitmate.Application.Workout.WorkoutService>();
builder.Services.AddScoped<IWeightService, Fitmate.Application.Workout.WeightService>();
// BE-170: nutrition meal log, schedule and AI scan stub.
builder.Services.AddScoped<INutritionStore, Fitmate.Infrastructure.Nutrition.EfNutritionStore>();
builder.Services.AddScoped<IAiNutritionAdapter, AiNutritionStubAdapter>();
builder.Services.AddScoped<INutritionService, NutritionService>();
builder.Services.AddScoped<IPushDeliveryDispatcher>(sp => new Fitmate.Infrastructure.Notifications.EfPushDeliveryDispatcher(
    sp.GetRequiredService<FitmateDbContext>(),
    sp.GetRequiredService<INotificationStore>(),
    sp.GetRequiredService<IDeviceStore>(),
    sp.GetRequiredService<IPushStore>(),
    sp.GetRequiredService<IOutboxDispatcher>(),
    sp.GetRequiredService<INotificationAuditLogger>(),
    sp.GetRequiredService<ILogger<Fitmate.Infrastructure.Notifications.EfPushDeliveryDispatcher>>()));
// Push provider: production wires MissingPushProvider until a real adapter is registered.
// (FCM / Expo adapters belong to BE-200c.)
builder.Services.AddSingleton<IPushProvider, Fitmate.Infrastructure.Notifications.MissingPushProvider>();
builder.Services.AddScoped<PushDeliverySweepService>();
builder.Services.AddHostedService<Fitmate.Infrastructure.Notifications.SweepBackgroundService>();
// Outbox handler for `notification.push_requested`.
builder.Services.AddScoped<IOutboxHandler, Fitmate.Application.Notifications.Handlers.NotificationPushHandler>();


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
// BE-380: dùng jwtOptions đã load + validate từ JwtOptionsLoader (cùng instance DI JwtTokenService/RefreshTokenService resolve).
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

// BE-040: Đảm bảo roles Member/Pt/Admin đã được seed (idempotent) trước khi request đầu tiên đến.
// SeedAdminCommand đã seed khi chạy CLI; với web host cần đảm bảo khi role được dùng trong [Authorize(Roles=...)].
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();
    foreach (var roleName in Fitmate.Api.Cli.SeedAdminCommand.RoleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Name = roleName });
    }
}

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
        // BE-040+: DomainException mang code + status + errors map → dùng trực tiếp, không guess.
        var (status, title, code, typeSlug, errors) = ex switch
        {
            Fitmate.Domain.Common.DomainException de => (
                de.HttpStatus,
                TitleForStatus(de.HttpStatus),
                de.Code,
                TypeSlugForCode(de.Code),
                de.Errors),
            ArgumentException => (400, "Bad request", ErrorCodes.GeneralBadRequest, "bad-request", (IReadOnlyDictionary<string, string[]>?)null),
            KeyNotFoundException => (404, "Resource not found", ErrorCodes.GeneralNotFound, "not-found", null),
            UnauthorizedAccessException => (401, "Unauthorized", ErrorCodes.AuthUnauthorized, "unauthorized", null),
            _ => (500, "Internal server error", ErrorCodes.GeneralInternal, "internal", null)
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
            traceId: traceId,
            errors: errors?.ToDictionary(kv => kv.Key, kv => kv.Value));

        await context.Response.WriteAsJsonAsync(problem);
    });
});

static string TitleForStatus(int status) => status switch
{
    400 => "Bad request",
    401 => "Unauthorized",
    403 => "Forbidden",
    404 => "Resource not found",
    409 => "Conflict",
    _ => "Error"
};

// typeSlug = lower-cased, dot→slash. "FITMATE.BOOKING.SLOT_TAKEN" → "booking/slot-taken"
static string TypeSlugForCode(string code)
{
    var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length <= 1) return code.ToLowerInvariant();
    // skip prefix "FITMATE"
    var slug = string.Join('/', parts.Skip(1).Take(parts.Length - 2).Select(p => p.ToLowerInvariant()));
    var last = parts[^1].ToLowerInvariant();
    return string.IsNullOrEmpty(slug) ? last : $"{slug}/{last}";
}

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
