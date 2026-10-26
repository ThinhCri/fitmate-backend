using Fitmate.Application.Common;
using Fitmate.Api.Cli;
using Fitmate.Api.Errors;
using Fitmate.Api.Idempotency;
using Fitmate.Api.Outbox;
using Fitmate.Application.Idempotency;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
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
using Fitmate.Application.Email;
using Fitmate.Application.Email.Handlers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Threading.RateLimiting;

Fitmate.Api.DotEnvLoader.Load();

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
        "seed-uat-data" => await SeedUatDataCommand.RunAsync(args[1..], config),
        "import-exercise-media" => await ImportExerciseCatalogMediaCommand.RunAsync(config),
        _ => UnknownCommand(args[0])
    };
    Environment.Exit(exitCode);
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"[fitmate-api] Unknown command: '{cmd}'. Supported: seed-admin, seed-perf-data, seed-uat-data, import-exercise-media");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<AdminReviewService>();
builder.Services.AddScoped<IAdminReviewStore, Fitmate.Infrastructure.Admin.EfAdminReviewStore>();

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
    var selected = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new();
    var payos = builder.Configuration.GetSection(PayosOptions.SectionName).Get<PayosOptions>() ?? new();
    var vnpay = builder.Configuration.GetSection(VnpayOptions.SectionName).Get<VnpayOptions>();
    var ready = selected.Provider == "payos" ? payos.Enabled && payos.IsValid()
        : selected.Provider == "vnpay" && vnpay?.IsValid() == true;
    if (!ready) throw new InvalidOperationException("Enabled production payments require a configured and approved selected provider.");
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
    opt.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Identity (DEC-032). BE-021: AddIdentityCore + Roles + EF stores.
// Dung AddIdentityCore thay vi AddIdentity de khong ep cookie auth; API chi dung JWT bearer.
// AddDataProtection cung cap IDataProtectionProvider cho AddDefaultTokenProviders
// (reset password + email confirm token).
// BE-300 & BE-825: persist key ring to a named volume / path so push-token encryption survives container recreates.
// DATA_PROTECTION_KEYS_PATH allows overriding for local test, isolated DR drills, or container volumes.
var dpKeysPath = builder.Configuration["DATA_PROTECTION_KEYS_PATH"]
    ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH")
    ?? "/home/fitmate/.aspnet/DataProtection-Keys";

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
    .SetApplicationName("Fitmate.Api");
builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.Password.RequiredLength = PasswordPolicy.MinimumLength;
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
builder.Services.AddScoped<IAuthSessionService, Fitmate.Infrastructure.Auth.EfAuthSessionService>();
builder.Services.AddScoped<IAdminLoginChallengeService, Fitmate.Infrastructure.Auth.EfAdminLoginChallengeService>();
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
builder.Services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailRequestValidator>();
builder.Services.AddScoped<IValidator<ResendEmailVerificationRequest>, ResendEmailVerificationRequestValidator>();

// Transactional email: SMTP transport + durable outbox handlers.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
if (builder.Environment.IsProduction())
{
    (builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions())
        .ValidateForProduction();
}
builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
builder.Services.AddSingleton<IEmailTokenProtector, Fitmate.Infrastructure.Email.DataProtectionEmailTokenProtector>();
builder.Services.AddScoped<IEmailSender, Fitmate.Infrastructure.Email.MailKitEmailSender>();
builder.Services.Configure<Fitmate.Infrastructure.Auth.SpeedSmsOptions>(
    builder.Configuration.GetSection(Fitmate.Infrastructure.Auth.SpeedSmsOptions.SectionName));
builder.Services.AddHttpClient<Fitmate.Infrastructure.Auth.SpeedSmsOtpSender>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<ISmsOtpSender>(sp =>
    builder.Configuration.GetValue<string>("Sms:Provider")?.Equals("SpeedSms", StringComparison.OrdinalIgnoreCase) == true
        ? sp.GetRequiredService<Fitmate.Infrastructure.Auth.SpeedSmsOtpSender>()
        : new Fitmate.Infrastructure.Auth.UnconfiguredSmsOtpSender());
builder.Services.Configure<Fitmate.Infrastructure.Trainers.GoogleMeetOptions>(
    builder.Configuration.GetSection(Fitmate.Infrastructure.Trainers.GoogleMeetOptions.SectionName));
builder.Services.AddHttpClient<Fitmate.Infrastructure.Trainers.GoogleMeetConnectionService>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IGoogleMeetConnectionService>(sp =>
    sp.GetRequiredService<Fitmate.Infrastructure.Trainers.GoogleMeetConnectionService>());
builder.Services.AddSingleton<IPhoneOtpProtector, Fitmate.Infrastructure.Auth.DataProtectionPhoneOtpProtector>();
builder.Services.AddScoped<IPhoneVerificationGateway, Fitmate.Infrastructure.Auth.IdentityPhoneVerificationGateway>();
builder.Services.AddScoped<PhoneVerificationService>();
builder.Services.AddScoped<IOutboxHandler, PhoneOtpOutboxHandler>();
builder.Services.AddScoped<IOutboxHandler, EmailVerificationOutboxHandler>();
builder.Services.AddScoped<IOutboxHandler, PasswordResetEmailOutboxHandler>();
builder.Services.AddScoped<IOutboxHandler, AccountDeletionEmailOutboxHandler>();
builder.Services.AddScoped<IOutboxHandler, SupportReplyEmailOutboxHandler>();
builder.Services.AddScoped<IOutboxHandler, SecurityWarningEmailOutboxHandler>();

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
builder.Services.AddScoped<IPtDisciplineGuard, Fitmate.Infrastructure.Trainers.EfPtDisciplineGuard>();

// BE-120 & BE-806: Trainer marketplace & reviews.
builder.Services.AddScoped<ITrainerMarketplaceStore, Fitmate.Infrastructure.Trainers.EfTrainerMarketplaceStore>();
builder.Services.AddScoped<ITrainerMarketplaceService, Fitmate.Application.Trainers.TrainerMarketplaceService>();
builder.Services.AddScoped<ITrainerReviewStore, Fitmate.Infrastructure.Trainers.EfTrainerReviewStore>();
builder.Services.AddScoped<ITrainerReviewService, Fitmate.Application.Trainers.TrainerReviewService>();

// BE-060: Booking lifecycle services.
builder.Services.AddScoped<IAppointmentStore, Fitmate.Infrastructure.Booking.EfAppointmentStore>();
builder.Services.AddScoped<IBookingHoldStore, Fitmate.Infrastructure.Booking.EfBookingHoldStore>();
builder.Services.AddScoped<ISessionPackageStore, Fitmate.Infrastructure.Booking.EfSessionPackageStore>();
builder.Services.AddScoped<SessionPackageHoldService>();
builder.Services.AddScoped<ISessionPackageReadStore, Fitmate.Infrastructure.Booking.EfSessionPackageReadStore>();
builder.Services.AddScoped<SessionPackageReadService>();
builder.Services.AddScoped<ISessionPackageStopStore, Fitmate.Infrastructure.Booking.EfSessionPackageStopStore>();
builder.Services.AddScoped<SessionPackageStopService>();
builder.Services.AddScoped<Fitmate.Infrastructure.Booking.SessionPackageExpirationService>();
builder.Services.AddScoped<IServiceBookingLookup, Fitmate.Infrastructure.Booking.ServiceBookingLookup>();
builder.Services.AddScoped<IBookingService, Fitmate.Application.Booking.BookingService>();
builder.Services.Configure<Fitmate.Application.Booking.RescheduleOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Booking.RescheduleOptions.SectionName));
builder.Services.AddScoped<IBookingRescheduleStore, Fitmate.Infrastructure.Booking.EfBookingRescheduleStore>();
builder.Services.AddScoped<IBookingRescheduleService, Fitmate.Application.Booking.BookingRescheduleService>();
builder.Services.AddScoped<IAdminBookingService, Fitmate.Application.Booking.AdminBookingService>();

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
builder.Services.AddScoped<IAccountDeletionOtpGateway, Fitmate.Infrastructure.Auth.IdentityAccountDeletionOtpGateway>();
builder.Services.AddScoped<IOutboxHandler, DeletionOtpEmailOutboxHandler>();
builder.Services.AddScoped<Fitmate.Application.Identity.AdminAccountDeletionService>();
builder.Services.AddSingleton(new Fitmate.Application.Identity.AccountDeletionFinalizationGate(
    builder.Configuration.GetValue<bool>("AccountDeletion:FinalizerEnabled")));
builder.Services.AddScoped<Fitmate.Infrastructure.Identity.AccountDeletionFinalizer>();

// BE-150 & BE-809: Admin finance.
builder.Services.AddScoped<IFinancialLedgerStore, Fitmate.Infrastructure.Finance.EfFinancialLedgerStore>();
builder.Services.AddScoped<IAdminFinanceStore, Fitmate.Infrastructure.Finance.EfAdminFinanceStore>();
builder.Services.AddScoped<IAdminFinanceService, Fitmate.Application.Finance.AdminFinanceService>();

// BE-810: Trainer revenue and settlements.
builder.Services.AddScoped<ITrainerRevenueStore, Fitmate.Infrastructure.Finance.EfTrainerRevenueStore>();
builder.Services.AddScoped<ITrainerRevenueService, Fitmate.Application.Finance.TrainerRevenueService>();
builder.Services.AddScoped<IManualSettlementStore, Fitmate.Infrastructure.Finance.EfManualSettlementStore>();
builder.Services.AddScoped<ManualSettlementService>();
builder.Services.AddScoped<IFinanceStepUp, Fitmate.Api.Admin.FinanceStepUp>();
builder.Services.Configure<Fitmate.Infrastructure.Finance.VietQrLookupOptions>(
    builder.Configuration.GetSection(Fitmate.Infrastructure.Finance.VietQrLookupOptions.SectionName));
builder.Services.AddHttpClient<Fitmate.Infrastructure.Finance.VietQrBankAccountLookup>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IBankAccountLookup>(sp =>
    sp.GetRequiredService<Fitmate.Infrastructure.Finance.VietQrBankAccountLookup>());
builder.Services.AddScoped<IPtPayoutAccountStore, Fitmate.Infrastructure.Finance.EfPtPayoutAccountStore>();
builder.Services.AddScoped<PtPayoutAccountService>();

// BE-151: Platform policy (read-only).
builder.Services.AddScoped<IPlatformPolicyService, Fitmate.Application.Finance.PlatformPolicyService>();

// BE-220: Admin metrics + action-required.
builder.Services.AddScoped<IAdminMetricsStore, Fitmate.Infrastructure.Admin.EfAdminMetricsStore>();
builder.Services.AddScoped<IAdminMetricsService, Fitmate.Application.Admin.AdminMetricsService>();
builder.Services.AddScoped<IAdminBusinessSummaryStore, Fitmate.Infrastructure.Admin.EfAdminBusinessSummaryStore>();
builder.Services.AddScoped<AdminBusinessSummaryService>();
builder.Services.AddScoped<IAdminTransactionsStore, Fitmate.Infrastructure.Admin.EfAdminTransactionsStore>();
builder.Services.AddScoped<AdminTransactionsService>();
builder.Services.AddScoped<IAdminRevenueReportStore, Fitmate.Infrastructure.Admin.EfAdminRevenueReportStore>();
builder.Services.AddScoped<AdminRevenueReportService>();
builder.Services.AddScoped<IAdminTrainerReviewsStore, Fitmate.Infrastructure.Admin.EfAdminTrainerReviewsStore>();
builder.Services.AddScoped<AdminTrainerReviewsService>();
builder.Services.AddOptions<Ga4Options>().Bind(builder.Configuration.GetSection(Ga4Options.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<Fitmate.Infrastructure.Admin.Ga4ReportProvider>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IGa4ReportProvider>(sp =>
    sp.GetRequiredService<Fitmate.Infrastructure.Admin.Ga4ReportProvider>());
builder.Services.AddScoped<AdminAnalyticsService>();
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
builder.Services.AddScoped<IAiChatStore, Fitmate.Infrastructure.Persistence.Chat.EfAiChatStore>();
builder.Services.AddScoped<IAiChatService, Fitmate.Application.Chat.AiChatService>();
builder.Services.Configure<Fitmate.Application.Ai.AiUsageOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Ai.AiUsageOptions.SectionName));
builder.Services.AddScoped<Fitmate.Application.Ai.IAiUsageService, Fitmate.Infrastructure.Ai.EfAiUsageService>();
builder.Services.AddOptions<Fitmate.Application.Ai.GooglePlayBillingOptions>()
    .Bind(builder.Configuration.GetSection(Fitmate.Application.Ai.GooglePlayBillingOptions.SectionName))
    .Validate(x => !x.SalesEnabled || x.Enabled,
        "Google Play AI Plus sales cannot open while purchase verification is disabled.")
    .Validate(x => !x.SalesEnabled ||
        (builder.Configuration.GetValue<bool>("Ai:Enabled") &&
         builder.Configuration.GetValue<bool>("Ai:FoodScanEnabled") &&
         builder.Configuration.GetValue<bool>("Ai:CompanionChatEnabled") &&
         builder.Configuration.GetValue<bool>("AiUsage:Enabled")),
        "Google Play AI Plus sales require both AI capabilities and usage enforcement.")
    .Validate(x => !x.SalesEnabled ||
        (((builder.Configuration["Ai:Provider"] == "python_service" &&
           Uri.TryCreate(builder.Configuration["Ai:BaseUrl"], UriKind.Absolute, out _) &&
           !string.IsNullOrWhiteSpace(builder.Configuration["Ai:InternalToken"])) ||
          (builder.Configuration["Ai:Provider"] == "gemini" &&
           builder.Configuration.GetValue<bool>("Gemini:Enabled") &&
           !string.IsNullOrWhiteSpace(builder.Configuration["Gemini:ApiKey"]))) &&
         ((builder.Configuration["Ai:CompanionProvider"] == "groq" &&
           builder.Configuration.GetValue<bool>("Groq:Enabled") &&
           !string.IsNullOrWhiteSpace(builder.Configuration["Groq:ApiKey"])) ||
          (builder.Configuration["Ai:CompanionProvider"] == "gemini" &&
           builder.Configuration.GetValue<bool>("Gemini:Enabled") &&
           !string.IsNullOrWhiteSpace(builder.Configuration["Gemini:ApiKey"])))),
        "Google Play AI Plus sales require configured real scan and chat providers.")
    .Validate(x => !x.Enabled ||
        (x.PackageName == "vn.fitmate.app" && x.ProductId == "fitmate_ai_plus" &&
         x.BasePlanId == "monthly" && !string.IsNullOrWhiteSpace(x.ServiceAccountJsonBase64)),
        "Google Play billing is enabled without the approved Android product or credentials.")
    .ValidateOnStart();
builder.Services.AddHttpClient<Fitmate.Infrastructure.Ai.GooglePlaySubscriptionVerifier>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<Fitmate.Application.Ai.IGooglePlaySubscriptionVerifier>(sp =>
    sp.GetRequiredService<Fitmate.Infrastructure.Ai.GooglePlaySubscriptionVerifier>());
builder.Services.AddScoped<Fitmate.Application.Ai.IAiSubscriptionService, Fitmate.Infrastructure.Ai.EfAiSubscriptionService>();
builder.Services.AddHostedService<Fitmate.Api.Ai.AiSubscriptionReconcileWorker>();

// BE-210+BE-211: Moderation (admin list/resolve/dismiss reports + warn/lock/delete).
builder.Services.AddScoped<IModerationStore, Fitmate.Infrastructure.Moderation.EfModerationStore>();
builder.Services.AddScoped<IModerationActionStore, Fitmate.Infrastructure.Moderation.EfModerationActionStore>();
builder.Services.AddScoped<IModerationService, Fitmate.Application.Moderation.ModerationService>();

// BE-200: Notifications — in-app inbox + push delivery (FCM/Expo on the wire).
// Push dispatcher commit strategy: each device row + notification row are saved in
// the same SaveChanges as the originating FriendService.SaveChangesAsync / ModerationService.WarnAsync.
// On startup the worker and sweep background service check `PushProviderConfig.Enabled` —
// when disabled they no-op cleanly so no deferred backlog accumulates.
// DataProtection key ring is persisted above via PersistKeysToFileSystem.
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
builder.Services.AddScoped<IExerciseCatalogStore, Fitmate.Infrastructure.Workout.EfExerciseCatalogStore>();
builder.Services.AddScoped<IWorkoutService, Fitmate.Application.Workout.WorkoutService>();
builder.Services.AddScoped<Fitmate.Application.Social.ISocialProfileSummaryStore, Fitmate.Infrastructure.Social.EfSocialProfileSummaryStore>();
builder.Services.AddScoped<Fitmate.Application.Social.SocialProfileSummaryService>();
builder.Services.AddScoped<IWeightService, Fitmate.Application.Workout.WeightService>();
builder.Services.AddScoped<IExerciseCatalogService, Fitmate.Application.Workout.ExerciseCatalogService>();
// BE-170 & BE-817: AI capability adapters & options. Disabled by default awaiting .pt handoff.
builder.Services.Configure<Fitmate.Application.Ai.AiOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Ai.AiOptions.SectionName));
builder.Services.Configure<Fitmate.Application.Ai.GeminiOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Ai.GeminiOptions.SectionName));
builder.Services.Configure<Fitmate.Application.Ai.GroqOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Ai.GroqOptions.SectionName));
builder.Services.AddScoped<INutritionStore, Fitmate.Infrastructure.Nutrition.EfNutritionStore>();
var aiOptions = builder.Configuration
    .GetSection(Fitmate.Application.Ai.AiOptions.SectionName)
    .Get<Fitmate.Application.Ai.AiOptions>() ?? new Fitmate.Application.Ai.AiOptions();
if (string.Equals(aiOptions.Provider, "python_service", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<Fitmate.Infrastructure.Nutrition.PythonAiNutritionAdapter>((sp, client) =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Fitmate.Application.Ai.AiOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30);
    });
    builder.Services.AddScoped<IAiNutritionAdapter>(sp =>
        sp.GetRequiredService<Fitmate.Infrastructure.Nutrition.PythonAiNutritionAdapter>());
}
else if (string.Equals(aiOptions.Provider, "gemini", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<Fitmate.Infrastructure.Nutrition.GeminiNutritionAdapter>(client => {
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.TimeoutSeconds, 5, 120));
        client.MaxResponseContentBufferSize = 1024 * 1024;
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddScoped<IAiNutritionAdapter>(sp => sp.GetRequiredService<Fitmate.Infrastructure.Nutrition.GeminiNutritionAdapter>());
}
else builder.Services.AddScoped<IAiNutritionAdapter, UnavailableNutritionAdapter>();
builder.Services.AddHttpClient<Fitmate.Infrastructure.Chat.GroqWorkoutAdapter>(client => {
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.TimeoutSeconds, 5, 120));
    client.MaxResponseContentBufferSize = 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<Fitmate.Application.Workout.IAiWorkoutAdapter>(sp => sp.GetRequiredService<Fitmate.Infrastructure.Chat.GroqWorkoutAdapter>());
builder.Services.AddScoped<Fitmate.Application.Workout.WorkoutSuggestionService>();
var geminiOptions = builder.Configuration
    .GetSection(Fitmate.Application.Ai.GeminiOptions.SectionName)
    .Get<Fitmate.Application.Ai.GeminiOptions>() ?? new Fitmate.Application.Ai.GeminiOptions();
var groqOptions = builder.Configuration
    .GetSection(Fitmate.Application.Ai.GroqOptions.SectionName)
    .Get<Fitmate.Application.Ai.GroqOptions>() ?? new Fitmate.Application.Ai.GroqOptions();
if (string.Equals(aiOptions.CompanionProvider, "groq", StringComparison.OrdinalIgnoreCase) && groqOptions.Enabled)
{
    builder.Services.AddHttpClient<Fitmate.Infrastructure.Chat.GroqCompanionAdapter>(client =>
        client.Timeout = TimeSpan.FromSeconds(groqOptions.TimeoutSeconds > 0 ? groqOptions.TimeoutSeconds : 60));
    if (geminiOptions.Enabled)
        builder.Services.AddHttpClient<Fitmate.Infrastructure.Chat.GeminiCompanionAdapter>(client =>
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(geminiOptions.TimeoutSeconds, 5, 120)));
    builder.Services.AddScoped<Fitmate.Application.Chat.IAiCompanionAdapter>(sp => new Fitmate.Application.Chat.CompanionRoutingAdapter(
        sp.GetRequiredService<Fitmate.Infrastructure.Chat.GroqCompanionAdapter>(),
        geminiOptions.Enabled ? sp.GetRequiredService<Fitmate.Infrastructure.Chat.GeminiCompanionAdapter>() : null));
}
else if (string.Equals(aiOptions.CompanionProvider, "gemini", StringComparison.OrdinalIgnoreCase) && geminiOptions.Enabled)
{
    builder.Services.AddHttpClient<Fitmate.Infrastructure.Chat.GeminiCompanionAdapter>(client =>
        client.Timeout = TimeSpan.FromSeconds(geminiOptions.TimeoutSeconds > 0 ? geminiOptions.TimeoutSeconds : 60));
    builder.Services.AddScoped<Fitmate.Application.Chat.IAiCompanionAdapter>(sp =>
        sp.GetRequiredService<Fitmate.Infrastructure.Chat.GeminiCompanionAdapter>());
}
else
{
    builder.Services.AddScoped<Fitmate.Application.Chat.IAiCompanionAdapter, Fitmate.Application.Chat.UnavailableCompanionAdapter>();
}
builder.Services.AddScoped<INutritionService, NutritionService>();
// BE-813: dashboard daily progress summary.
builder.Services.AddScoped<Fitmate.Application.Dashboard.IDashboardStore, Fitmate.Infrastructure.Dashboard.EfDashboardStore>();
builder.Services.AddScoped<Fitmate.Application.Dashboard.IDashboardService, Fitmate.Application.Dashboard.DashboardService>();
// BE-819: Support, Preferences & Capabilities.
builder.Services.AddScoped<Fitmate.Application.Support.ISupportTicketStore, Fitmate.Infrastructure.Support.EfSupportTicketStore>();
builder.Services.AddScoped<Fitmate.Application.Support.ISupportService, Fitmate.Application.Support.SupportService>();
builder.Services.AddScoped<Fitmate.Application.Preferences.IUserPreferenceStore, Fitmate.Infrastructure.Preferences.EfUserPreferenceStore>();
builder.Services.AddScoped<Fitmate.Application.Preferences.IUserPreferenceService, Fitmate.Application.Preferences.UserPreferenceService>();
// BE-140, BE-808 & BE-400: payment provider wiring & gating
var paymentOptions = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.SectionName));

// BE-400: VNPay configuration, security, client & adapter
builder.Services.Configure<Fitmate.Application.Payments.VnpayOptions>(
    builder.Configuration.GetSection(Fitmate.Application.Payments.VnpayOptions.SectionName));
builder.Services.AddSingleton<Fitmate.Application.Payments.IVnpaySecurity, Fitmate.Infrastructure.Payments.Vnpay.VnpaySecurity>();
builder.Services.AddHttpClient<Fitmate.Infrastructure.Payments.Vnpay.IVnpayClient, Fitmate.Infrastructure.Payments.Vnpay.VnpayClient>((sp, client) =>
{
    var vnpayOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Fitmate.Application.Payments.VnpayOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(vnpayOpts.TimeoutSeconds > 0 ? vnpayOpts.TimeoutSeconds : 30);
});

builder.Services.AddScoped<IPaymentStore, Fitmate.Infrastructure.Payments.EfPaymentStore>();

var vnpayConfig = builder.Configuration.GetSection(Fitmate.Application.Payments.VnpayOptions.SectionName).Get<Fitmate.Application.Payments.VnpayOptions>();
var payosConfig = builder.Configuration.GetSection(PayosOptions.SectionName).Get<PayosOptions>() ?? new PayosOptions();
builder.Services.Configure<PayosOptions>(builder.Configuration.GetSection(PayosOptions.SectionName));
builder.Services.AddHttpClient<Fitmate.Infrastructure.Payments.Payos.PayosPaymentAdapter>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IPayosWebhookHandler, Fitmate.Infrastructure.Payments.Payos.PayosWebhookHandler>();
if (paymentOptions.AllowMockProvider)
    throw new InvalidOperationException("Runtime mock payments are removed. Configure a real provider; test doubles belong only in test DI.");
if (payosConfig.Enabled && (!payosConfig.IsValid() || paymentOptions.Provider != "payos"))
    throw new InvalidOperationException("Enabled payOS requires an approved merchant, complete credentials and Payment:Provider=payos.");
if (paymentOptions.Provider == "payos" && payosConfig.Enabled)
{
    builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<Fitmate.Infrastructure.Payments.Payos.PayosPaymentAdapter>());
}
else if (paymentOptions.Provider == "vnpay" && vnpayConfig != null && vnpayConfig.IsValid())
{
    builder.Services.AddScoped<IPaymentProvider, Fitmate.Infrastructure.Payments.Vnpay.VnpayPaymentAdapter>();
}
else
{
    // Fail-closed in production when valid merchant credentials are not configured
    builder.Services.AddScoped<IPaymentProvider>(sp =>
        new MissingPaymentProvider("Payment provider is not configured. Production requires valid merchant credentials."));
}
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPackagePaymentStore, Fitmate.Infrastructure.Payments.EfPackagePaymentStore>();
builder.Services.AddScoped<PackagePaymentCheckoutService>();
builder.Services.AddScoped<IPushDeliveryDispatcher>(sp => new Fitmate.Infrastructure.Notifications.EfPushDeliveryDispatcher(
    sp.GetRequiredService<FitmateDbContext>(),
    sp.GetRequiredService<INotificationStore>(),
    sp.GetRequiredService<IDeviceStore>(),
    sp.GetRequiredService<IPushStore>(),
    sp.GetRequiredService<IOutboxDispatcher>(),
    sp.GetRequiredService<INotificationAuditLogger>(),
    sp.GetRequiredService<ILogger<Fitmate.Infrastructure.Notifications.EfPushDeliveryDispatcher>>()));
builder.Services.AddOptions<Fitmate.Infrastructure.Notifications.FcmPushOptions>()
    .Bind(builder.Configuration.GetSection(Fitmate.Infrastructure.Notifications.FcmPushOptions.SectionName))
    .Validate(Fitmate.Infrastructure.Notifications.FcmPushProvider.ValidConfiguration,
        "Enabled FCM requires a valid matching Firebase project and service-account credential.")
    .ValidateOnStart();
if (builder.Configuration.GetValue<bool>("FcmPush:Enabled"))
{
    builder.Services.AddHttpClient<Fitmate.Infrastructure.Notifications.FcmPushProvider>(client => client.Timeout = TimeSpan.FromSeconds(20))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddScoped<IPushProvider>(sp => sp.GetRequiredService<Fitmate.Infrastructure.Notifications.FcmPushProvider>());
}
else builder.Services.AddSingleton<IPushProvider, Fitmate.Infrastructure.Notifications.MissingPushProvider>();
builder.Services.AddScoped<PushDeliverySweepService>();
builder.Services.AddHostedService<Fitmate.Infrastructure.Notifications.SweepBackgroundService>();
builder.Services.AddHostedService<Fitmate.Infrastructure.Booking.BookingExpirationBackgroundService>();
// Outbox handler for `notification.push_requested`.
builder.Services.AddScoped<Fitmate.Application.Notifications.Handlers.NotificationPushHandler>();
builder.Services.AddScoped<IOutboxHandler>(sp => sp.GetRequiredService<Fitmate.Application.Notifications.Handlers.NotificationPushHandler>());
builder.Services.AddHostedService<Fitmate.Infrastructure.Notifications.PushRetryBackgroundService>();

// BE-816: local storage for development, Cloudflare R2 for durable environments.
var storageBackend = (builder.Configuration["STORAGE_BACKEND"]
    ?? builder.Configuration["Storage:Backend"]
    ?? "local").Trim().ToLowerInvariant();
builder.Services.Configure<Fitmate.Application.Storage.StorageOptions>(options =>
{
    builder.Configuration.GetSection(Fitmate.Application.Storage.StorageOptions.SectionName).Bind(options);
    options.Backend = storageBackend;
    options.BasePath = builder.Configuration["STORAGE_LOCAL_PATH"] ?? options.BasePath;
    options.BaseUrl = builder.Configuration["STORAGE_PUBLIC_BASE_URL"] ?? options.BaseUrl;
    options.R2AccountId = builder.Configuration["R2_ACCOUNT_ID"] ?? options.R2AccountId;
    options.R2AccessKeyId = builder.Configuration["R2_ACCESS_KEY_ID"] ?? options.R2AccessKeyId;
    options.R2SecretAccessKey = builder.Configuration["R2_SECRET_ACCESS_KEY"] ?? options.R2SecretAccessKey;
    options.R2BucketName = builder.Configuration["R2_BUCKET_NAME"] ?? options.R2BucketName;
});

if (storageBackend == "r2")
{
    var accountId = builder.Configuration["R2_ACCOUNT_ID"] ?? builder.Configuration["Storage:R2AccountId"];
    var accessKeyId = builder.Configuration["R2_ACCESS_KEY_ID"] ?? builder.Configuration["Storage:R2AccessKeyId"];
    var secretAccessKey = builder.Configuration["R2_SECRET_ACCESS_KEY"] ?? builder.Configuration["Storage:R2SecretAccessKey"];
    var bucketName = builder.Configuration["R2_BUCKET_NAME"] ?? builder.Configuration["Storage:R2BucketName"];
    if (new[] { accountId, accessKeyId, secretAccessKey, bucketName }.Any(string.IsNullOrWhiteSpace))
        throw new InvalidOperationException("R2 storage requires R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY and R2_BUCKET_NAME.");

    builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(_ => new Amazon.S3.AmazonS3Client(
        new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey),
        new Amazon.S3.AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        }));
    builder.Services.AddSingleton<Fitmate.Application.Storage.IStorageService, Fitmate.Infrastructure.Storage.R2StorageService>();
    builder.Services.AddSingleton<Fitmate.Application.Storage.ISignedUrlService, Fitmate.Infrastructure.Storage.R2SignedUrlService>();
}
else if (storageBackend == "local")
{
    builder.Services.AddSingleton<Fitmate.Application.Storage.IStorageService, Fitmate.Infrastructure.Storage.LocalStorageService>();
    builder.Services.AddSingleton<Fitmate.Application.Storage.ISignedUrlService, Fitmate.Infrastructure.Storage.HmacSignedUrlService>();
}
else
{
    throw new InvalidOperationException("STORAGE_BACKEND must be 'local' or 'r2'.");
}
builder.Services.AddSingleton<Fitmate.Application.Storage.IOwnedStorageKeyListing>(sp =>
    (Fitmate.Application.Storage.IOwnedStorageKeyListing)sp.GetRequiredService<Fitmate.Application.Storage.IStorageService>());
builder.Services.AddHostedService<Fitmate.Infrastructure.Identity.AccountDeletionFinalizationWorker>();


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
        // Keep JWT wire claim names (sid, amr, security_stamp) unchanged.
        // The default legacy inbound mapping rewrites sid and breaks server-side session lookup.
        opt.MapInboundClaims = false;
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/realtime"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                // MapInboundClaims is disabled so wire claim names stay stable.
                // Resolve the JWT-native `sub` claim first; keep the legacy URI
                // fallback for tokens issued before the mapping was hardened.
                var id = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = id is null ? null : await users.FindByIdAsync(id);
                if (user is null || user.IsDeleted || user.Status != Fitmate.Domain.Common.UserStatus.Active ||
                    await users.IsLockedOutAsync(user) ||
                    principal.FindFirst("security_stamp")?.Value != user.SecurityStamp)
                {
                    context.Fail("Session revoked or account inactive.");
                    return;
                }
                var method = principal.FindFirst("amr")?.Value ?? principal.FindFirst(System.Security.Claims.ClaimTypes.AuthenticationMethod)?.Value;
                if (!principal.IsInRole("MfaSetup"))
                {
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<IAuthSessionService>();
                    var active = Guid.TryParse(principal.FindFirst("sid")?.Value, out var sid)
                        ? await sessions.FindActiveAsync(sid, user.Id, user.SecurityStamp!, context.HttpContext.RequestAborted) : null;
                    if (active is null || active.AuthenticationMethod != method) { context.Fail("Session revoked."); return; }
                    var roles = await users.GetRolesAsync(user);
                    if (principal.IsInRole("Admin") && !roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
                    { context.Fail("Role revoked."); return; }
                }
                if (principal.IsInRole("Admin") && (!user.TwoFactorEnabled || method != "mfa"))
                    context.Fail("Admin MFA required.");
                if (principal.IsInRole("MfaSetup") && user.TwoFactorEnabled)
                    context.Fail("Enrollment session has ended.");
                if (principal.IsInRole("MfaSetup") &&
                    context.Request.Path != "/api/v1/admin/auth/mfa/setup" &&
                    context.Request.Path != "/api/v1/admin/auth/mfa/verify")
                    context.Fail("Enrollment token cannot access application data.");
            }
        };
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role",
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
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(ip, prefix));
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

builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, Fitmate.Api.Realtime.FitmateUserIdProvider>();
builder.Services.AddSingleton<Fitmate.Application.Realtime.IRealtimeNotifier, Fitmate.Api.Realtime.SignalRRealtimeNotifier>();

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
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
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
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.PerMinutePerIp,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("GeneralLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
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
                code: Fitmate.Api.Errors.ErrorCodes.ValidationFailed,
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

// Interactive API exploration is available only on local development hosts.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Fitmate API", Version = "v1",
            Description = "Development API explorer. Requests execute against the running database."
        });
        options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer", BearerFormat = "JWT",
            Description = "Paste the access token returned by login (without the Bearer prefix)."
        });
        options.OperationFilter<Fitmate.Api.OpenApi.DevelopmentOperationFilter>();
    });
}

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
            // BE-080 wire codes: namespace Api.Errors, không phải domain ErrorCodes.
            ArgumentException => (400, "Bad request", Fitmate.Api.Errors.ErrorCodes.GeneralBadRequest, "bad-request", (IReadOnlyDictionary<string, string[]>?)null),
            KeyNotFoundException => (404, "Resource not found", Fitmate.Api.Errors.ErrorCodes.GeneralNotFound, "not-found", null),
            UnauthorizedAccessException => (401, "Unauthorized", Fitmate.Api.Errors.ErrorCodes.AuthUnauthorized, "unauthorized", null),
            _ => (500, "Internal server error", Fitmate.Api.Errors.ErrorCodes.GeneralInternal, "internal", null)
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("v1/swagger.json", "Fitmate API v1");
        options.RoutePrefix = "swagger";
    });
}

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

// BE-030: JWT auth pipeline.
app.UseAuthentication();

// Authenticated traffic is rate-limited per account; anonymous/auth endpoints
// remain partitioned by IP so multiple devices on one Wi-Fi do not share one
// general API budget after login.
app.UseRateLimiter();
app.UseAuthorization();

// BE-040: Đảm bảo roles Member/Pt/Admin đã được seed (idempotent) trước khi request đầu tiên đến.
// SeedAdminCommand đã seed khi chạy CLI; với web host cần đảm bảo khi role được dùng trong [Authorize(Roles=...)].
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();
    var roleSeedStrategy = db.Database.CreateExecutionStrategy();
    await roleSeedStrategy.ExecuteAsync(async () =>
    {
        await using var roleSeedTransaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('fitmate-role-seed', 0))");
        foreach (var roleName in Fitmate.Api.Cli.SeedAdminCommand.RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Name = roleName });
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Unable to seed role '{roleName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }
        await roleSeedTransaction.CommitAsync();
    });
}

// BE-070: Idempotency middleware (DEC-011). Áp dụng cho mọi mutation có Idempotency-Key header.
// Đặt SAU authentication để chỉ nhận key đã qua auth filter, nhưng TRƯỚC MapControllers
// để có thể short-circuit khi replay/conflict.
app.UseMiddleware<IdempotencyMiddleware>();

// BE-420: HMAC-signed-URL middleware (gates /uploads/*). Must be BEFORE UseStaticFiles so
// invalid/expired URLs short-circuit before the file system is touched.
if (storageBackend == "local")
{
    app.UseMiddleware<Fitmate.Api.Storage.SignedUrlMiddleware>();
    var storageBasePath = Path.GetFullPath(builder.Configuration["STORAGE_LOCAL_PATH"]
        ?? builder.Configuration["Storage:BasePath"] ?? "./uploads");
    Directory.CreateDirectory(storageBasePath);
    var storageBaseUrl = (builder.Configuration["STORAGE_PUBLIC_BASE_URL"]
        ?? builder.Configuration["Storage:BaseUrl"] ?? "http://localhost:5116/uploads").TrimEnd('/');
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
}

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
app.MapHub<Fitmate.Api.Realtime.FitmateRealtimeHub>("/hubs/realtime");

app.Run();

public partial class Program { }
