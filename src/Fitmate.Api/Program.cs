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
using Fitmate.Application.Payments;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

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
        "seed-perf-data" => await SeedPerfDataCommand.RunAsync(args[1..], config),
        _ => UnknownCommand(args[0])
    };
    Environment.Exit(exitCode);
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"[fitmate-api] Unknown command: '{cmd}'. Supported: seed-admin, seed-perf-data");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

// BE-822: Kestrel body size limit (10MB)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

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

// BE-822: Fail-closed configuration check
// In production, when payment feature is enabled, real gateway credentials must be configured.
if (builder.Environment.IsProduction() && string.Equals(builder.Configuration["FEATURE_PAYMENT_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
{
    var vnpaySecret = builder.Configuration["VNPAY_HASH_SECRET"];
    if (string.IsNullOrWhiteSpace(vnpaySecret) || vnpaySecret.StartsWith("__SET_"))
    {
        throw new InvalidOperationException("Fail-closed: In production, FEATURE_PAYMENT_ENABLED is true but VNPAY_HASH_SECRET is not configured.");
    }
}

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
    .AddSignInManager() // SignInManager required by AuthController (login flow)
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

// BE-030 & BE-802: FluentValidation auto-validate cho Controller.
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();
builder.Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();

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

// BE-120 & BE-806: Trainer marketplace & reviews.
builder.Services.AddScoped<ITrainerMarketplaceStore, Fitmate.Infrastructure.Trainers.EfTrainerMarketplaceStore>();
builder.Services.AddScoped<ITrainerMarketplaceService, Fitmate.Application.Trainers.TrainerMarketplaceService>();
builder.Services.AddScoped<ITrainerReviewStore, Fitmate.Infrastructure.Trainers.EfTrainerReviewStore>();
builder.Services.AddScoped<ITrainerReviewService, Fitmate.Application.Trainers.TrainerReviewService>();

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

// BE-804: User profile and search.
builder.Services.AddScoped<IUserProfileStore, Fitmate.Infrastructure.Identity.EfUserProfileStore>();
builder.Services.AddScoped<IUserProfileService, Fitmate.Application.Identity.UserProfileService>();

// BE-805: Account deletion and data retention.
builder.Services.AddSingleton<IDeletionTokenProtector, Fitmate.Infrastructure.Identity.DataProtectionDeletionTokenProtector>();
builder.Services.AddScoped<IAccountDeletionStore, Fitmate.Infrastructure.Identity.EfAccountDeletionStore>();
builder.Services.AddScoped<IAccountDeletionService, Fitmate.Application.Identity.AccountDeletionService>();

// BE-150 & BE-809: Admin finance.
builder.Services.AddScoped<IAdminFinanceStore, Fitmate.Infrastructure.Finance.EfAdminFinanceStore>();
builder.Services.AddScoped<IAdminFinanceService, Fitmate.Application.Finance.AdminFinanceService>();

// BE-810: Trainer revenue and settlements.
builder.Services.AddScoped<ITrainerRevenueStore, Fitmate.Infrastructure.Finance.EfTrainerRevenueStore>();
builder.Services.AddScoped<ITrainerRevenueService, Fitmate.Application.Finance.TrainerRevenueService>();

// BE-151: Platform policy (read-only).
builder.Services.AddScoped<IPlatformPolicyService, Fitmate.Application.Finance.PlatformPolicyService>();

// BE-220: Admin metrics + action-required.
builder.Services.AddScoped<IAdminMetricsStore, Fitmate.Infrastructure.Admin.EfAdminMetricsStore>();
builder.Services.AddScoped<IAdminMetricsService, Fitmate.Application.Admin.AdminMetricsService>();
builder.Services.AddScoped<Fitmate.Application.Admin.IAdminOutboxService, Fitmate.Application.Admin.AdminOutboxService>();

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
// BE-170 & BE-817: AI capability adapters & options. Disabled by default awaiting .pt handoff.
builder.Services.Configure<Fitmate.Application.Ai.AiOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Ai.AiOptions.SectionName));
builder.Services.AddScoped<INutritionStore, Fitmate.Infrastructure.Nutrition.EfNutritionStore>();
builder.Services.AddScoped<IAiNutritionAdapter, AiNutritionStubAdapter>();
builder.Services.AddScoped<Fitmate.Application.Workout.IAiWorkoutAdapter, Fitmate.Application.Workout.AiWorkoutStubAdapter>();
builder.Services.AddScoped<Fitmate.Application.Chat.IAiCompanionAdapter, Fitmate.Application.Chat.AiCompanionStubAdapter>();
builder.Services.AddScoped<INutritionService, NutritionService>();
// BE-813: dashboard daily progress summary.
builder.Services.AddScoped<Fitmate.Application.Dashboard.IDashboardStore, Fitmate.Infrastructure.Dashboard.EfDashboardStore>();
builder.Services.AddScoped<Fitmate.Application.Dashboard.IDashboardService, Fitmate.Application.Dashboard.DashboardService>();
// BE-819: Support, Preferences & Capabilities.
builder.Services.AddScoped<Fitmate.Application.Support.ISupportTicketStore, Fitmate.Infrastructure.Support.EfSupportTicketStore>();
builder.Services.AddScoped<Fitmate.Application.Support.ISupportService, Fitmate.Application.Support.SupportService>();
builder.Services.AddScoped<Fitmate.Application.Preferences.IUserPreferenceStore, Fitmate.Infrastructure.Preferences.EfUserPreferenceStore>();
builder.Services.AddScoped<Fitmate.Application.Preferences.IUserPreferenceService, Fitmate.Application.Preferences.UserPreferenceService>();
// BE-140 & BE-808: payment provider gating (StubPaymentProvider in dev/test explicit, MissingPaymentProvider in production).
var paymentOptions = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.SectionName));
builder.Services.AddScoped<IPaymentStore, Fitmate.Infrastructure.Payments.EfPaymentStore>();
if (builder.Environment.IsDevelopment() || paymentOptions.AllowMockProvider)
{
    builder.Services.AddScoped<IPaymentProvider, StubPaymentProvider>();
}
else
{
    builder.Services.AddScoped<IPaymentProvider>(sp =>
        new MissingPaymentProvider("Payment provider is not configured. Production requires valid merchant credentials."));
}
builder.Services.AddScoped<IPaymentService, PaymentService>();
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

// BE-420: Storage adapter (local volume + HMAC presigned URL).
builder.Services.Configure<Fitmate.Application.Storage.StorageOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Storage.StorageOptions.SectionName));
builder.Services.AddSingleton<Fitmate.Application.Storage.IStorageService, Fitmate.Infrastructure.Storage.LocalStorageService>();
builder.Services.AddSingleton<Fitmate.Application.Storage.ISignedUrlService, Fitmate.Infrastructure.Storage.HmacSignedUrlService>();


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
builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// BE-822: Forwarded Headers for reverse proxy support.
// Do NOT clear KnownNetworks/KnownProxies: loopback is trusted by default,
// additional trusted proxies/networks can be configured via environment variables.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var knownProxiesEnv = builder.Configuration["FORWARDED_KNOWN_PROXIES"] ?? builder.Configuration["TRUSTED_PROXIES"];
    if (!string.IsNullOrWhiteSpace(knownProxiesEnv))
    {
        foreach (var proxy in knownProxiesEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }
    var knownNetworksEnv = builder.Configuration["FORWARDED_KNOWN_NETWORKS"];
    if (!string.IsNullOrWhiteSpace(knownNetworksEnv))
    {
        foreach (var network in knownNetworksEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = network.Split('/');
            if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var prefix))
            {
                options.KnownNetworks.Add(new IPNetwork(ip, prefix));
            }
        }
    }
});

// BE-822: CORS policy with production fail-closed enforcement
var rawCorsOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"];
string[] corsOrigins;

if (builder.Environment.IsProduction())
{
    if (string.IsNullOrWhiteSpace(rawCorsOrigins))
    {
        throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS must be explicitly configured.");
    }
    corsOrigins = rawCorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (corsOrigins.Length == 0 || corsOrigins.Any(o => o == "*" || o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1")))
    {
        throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS cannot contain wildcards ('*') or localhost.");
    }
}
else
{
    corsOrigins = (rawCorsOrigins ?? "http://localhost:5173,http://localhost:8080")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FitmateCorsPolicy", policy =>
    {
        if (corsOrigins.Any(o => o == "*"))
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// BE-822: Built-in Rate Limiting (.NET 8)
var rateLimitOpts = builder.Configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();
if (int.TryParse(builder.Configuration["RATE_LIMIT_PER_MINUTE_PER_IP"], out var perMin))
    rateLimitOpts.PerMinutePerIp = perMin;
if (int.TryParse(builder.Configuration["RATE_LIMIT_AUTH_PER_MINUTE_PER_IP"], out var authPerMin))
    rateLimitOpts.AuthPerMinutePerIp = authPerMin;
if (int.TryParse(builder.Configuration["RATE_LIMIT_AUTH_REFRESH_PER_MINUTE_PER_IP"], out var authRefreshPerMin))
    rateLimitOpts.AuthRefreshPerMinutePerIp = authRefreshPerMin;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var traceId = FitmateProblemDetails.ResolveTraceId(context.HttpContext);
        var problem = FitmateProblemDetails.Create(
            status: StatusCodes.Status429TooManyRequests,
            title: "Too Many Requests",
            code: Fitmate.Api.Errors.ErrorCodes.GeneralRateLimited,
            typeSlug: "rate-limited",
            detail: "Rate limit exceeded. Please try again later.",
            traceId: traceId);
        await context.HttpContext.Response.WriteAsJsonAsync(
            problem,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: token);
    };

    // GlobalLimiter applies across all endpoints by default
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.PerMinutePerIp,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("GeneralLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.PerMinutePerIp,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("AuthLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.AuthPerMinutePerIp,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("AuthRefreshLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.AuthRefreshPerMinutePerIp,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

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

// BE-824: CorrelationIdMiddleware gắn CorrelationId và TraceIdentifier xuyên suốt pipeline
app.UseMiddleware<Fitmate.Api.Observability.CorrelationIdMiddleware>();

// BE-080: Global exception handler (RFC 7807) đặt đầu pipeline để bắt lỗi toàn diện.
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

        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    });
});

// BE-822: Forwarded Headers middleware (must run before rate limiting and auth)
app.UseForwardedHeaders();

// BE-822: Security Headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-XSS-Protection"] = "0";
    await next();
});

// BE-822: CORS middleware
app.UseCors("FitmateCorsPolicy");

// BE-822: Rate Limiter middleware
app.UseRateLimiter();

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

// BE-420: HMAC-signed-URL middleware (gates /uploads/*). Must be BEFORE UseStaticFiles so
// invalid/expired URLs short-circuit before the file system is touched.
app.UseMiddleware<Fitmate.Api.Storage.SignedUrlMiddleware>();
// Static files served from StorageOptions.BasePath. The request path segment is derived
// from StorageOptions.BaseUrl (the part after the last '/').
var storageBasePath = Path.GetFullPath(builder.Configuration["Storage:BasePath"] ?? "./uploads");
Directory.CreateDirectory(storageBasePath);
var storageBaseUrl = (builder.Configuration["Storage:BaseUrl"] ?? "http://localhost:5116/uploads").TrimEnd('/');
var storageRequestPath = storageBaseUrl[(storageBaseUrl.LastIndexOf('/') + 1)..];
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".jpg"] = "image/jpeg";
contentTypeProvider.Mappings[".png"] = "image/png";
contentTypeProvider.Mappings[".webp"] = "image/webp";
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storageBasePath),
    RequestPath = "/" + storageRequestPath,
    ServeUnknownFileTypes = false,
    ContentTypeProvider = contentTypeProvider
});

static string TitleForStatus(int status) => status switch
{
    400 => "Bad request",
    401 => "Unauthorized",
    403 => "Forbidden",
    404 => "Resource not found",
    409 => "Conflict",
    429 => "Too Many Requests",
    502 => "Bad Gateway",
    503 => "Service Unavailable",
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

public partial class Program { }
