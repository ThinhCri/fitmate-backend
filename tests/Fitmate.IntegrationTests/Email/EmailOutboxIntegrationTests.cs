using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Email.Handlers;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.IntegrationTests.Email;

/// <summary>
/// Integration tests: OutboxWorker + Email handlers wired together.
/// Uses mock/stub SMTP (no real MailHog). Real MailHog tests are BLOCKED OPS-02.
/// </summary>
public class EmailOutboxIntegrationTests
{
    private readonly FakeEmailSender _sender = new();
    private readonly FakeEmailTokenProtector _protector = new();
    private readonly EmailTemplateRenderer _renderer = new();
    private readonly IOptions<EmailOptions> _options;

    public EmailOutboxIntegrationTests()
    {
        _options = Options.Create(new EmailOptions
        {
            FrontendBaseUrl = "https://fitmate.vn",
            SmtpHost = "localhost"
        });
    }

    [Fact]
    public async Task OutboxWorker_PasswordResetHandler_SendsEmailAndMarksDone()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "user@example.com",
            FullName = "Test User",
            Status = Domain.Common.UserStatus.Active
        };

        var userManager = new FakeUserManager(id => Task.FromResult(id == userId.ToString() ? user : null));

        var handler = new PasswordResetEmailOutboxHandler(
            _sender, _renderer, _protector,
            userManager, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            userId, "user@example.com", "prot:plain-token", "Test User",
            DateTime.UtcNow.AddMinutes(10));

        var msg = OutboxMessage.Enqueue(
            "auth.password_reset_requested",
            JsonSerializer.Serialize(payload));

        var outboxStore = new InMemoryOutboxStore(new[] { msg });
        var worker = new OutboxWorker(
            outboxStore, new[] { handler },
            NullLogger<OutboxWorker>.Instance);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal("done", msg.Status);
        Assert.Single(_sender.SentMessages);
        Assert.Equal("user@example.com", _sender.SentMessages[0].ToEmail);
    }

    [Fact]
    public async Task OutboxWorker_TerminalException_MovesToDeadLetter()
    {
        var userManager = new FakeUserManager(_ => Task.FromResult<ApplicationUser?>(null));

        var handler = new PasswordResetEmailOutboxHandler(
            _sender, _renderer, _protector,
            userManager, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            Guid.NewGuid(), "test@example.com", "prot:token", "Test",
            DateTime.UtcNow.AddMinutes(-5)); // expired

        var msg = OutboxMessage.Enqueue(
            "auth.password_reset_requested",
            JsonSerializer.Serialize(payload));

        var outboxStore = new InMemoryOutboxStore(new[] { msg });
        var worker = new OutboxWorker(
            outboxStore, new[] { handler },
            NullLogger<OutboxWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("dead_letter", msg.Status);
        Assert.Contains("TOKEN_EXPIRED", msg.LastError);
        Assert.Empty(_sender.SentMessages);

        // Terminal messages must not be selected for another automatic attempt.
        Assert.Equal(0, await worker.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal("dead_letter", msg.Status);
    }

    [Fact]
    public async Task OutboxWorker_TransientError_RetriesWithBackoff()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "user@example.com",
            FullName = "Test",
            Status = Domain.Common.UserStatus.Active
        };

        var userManager = new FakeUserManager(id => Task.FromResult(id == userId.ToString() ? user : null));

        var failingSender = new FakeEmailSender
        {
            ThrowOnSend = new InvalidOperationException("Transient SMTP failure")
        };

        var handler = new PasswordResetEmailOutboxHandler(
            failingSender, _renderer, _protector,
            userManager, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            userId, "user@example.com", "prot:token", "Test",
            DateTime.UtcNow.AddMinutes(10));

        var msg = OutboxMessage.Enqueue(
            "auth.password_reset_requested",
            JsonSerializer.Serialize(payload));

        var outboxStore = new InMemoryOutboxStore(new[] { msg });
        var worker = new OutboxWorker(
            outboxStore, new[] { handler },
            NullLogger<OutboxWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("pending", msg.Status);
        Assert.Equal(1, msg.Attempts);
        Assert.True(msg.NextAttemptAt > msg.CreatedAt);
    }

    [Fact]
    public async Task OutboxWorker_NoHandler_MarksFailedNotDone()
    {
        var msg = OutboxMessage.Enqueue(
            "unknown.event.type",
            "{}");

        var outboxStore = new InMemoryOutboxStore(new[] { msg });
        var worker = new OutboxWorker(
            outboxStore, Array.Empty<IOutboxHandler>(),
            NullLogger<OutboxWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.NotEqual("done", msg.Status);
        Assert.Contains("NO_HANDLER", msg.LastError);
    }
}

internal class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> SentMessages { get; } = new();
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (ThrowOnSend != null) throw ThrowOnSend;
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}

internal class FakeEmailTokenProtector : IEmailTokenProtector
{
    public string Protect(string plainToken) => $"prot:{plainToken}";
    public string Unprotect(string protectedToken) =>
        protectedToken.StartsWith("prot:") ? protectedToken[5..] : protectedToken;
}

internal class FakeUserManager : UserManager<ApplicationUser>
{
    private readonly Func<string, Task<ApplicationUser?>> _findById;

    public FakeUserManager(Func<string, Task<ApplicationUser?>> findById)
        : base(new FakeUserStore(), null!, null!, null!, null!, null!, null!, null!, null!)
    {
        _findById = findById;
    }

    public override Task<ApplicationUser?> FindByIdAsync(string userId) => _findById(userId);
}

internal class FakeUserStore : IUserStore<ApplicationUser>
{
    public void Dispose() { }
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id.ToString());
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) => Task.CompletedTask;
    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
}

/// <summary>
/// In-memory outbox store for integration tests.
/// </summary>
internal class InMemoryOutboxStore : IOutboxStore
{
    private readonly List<OutboxMessage> _messages;

    public InMemoryOutboxStore(IEnumerable<OutboxMessage> messages)
    {
        _messages = messages.ToList();
    }

    public Task<IReadOnlyList<OutboxMessage>> FetchDueAsync(int batchSize, DateTime nowUtc, CancellationToken ct)
    {
        var due = _messages
            .Where(m => m.Status == "pending" && m.NextAttemptAt <= nowUtc)
            .Take(batchSize)
            .ToList();
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(due);
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<(IReadOnlyList<OutboxMessage> Items, int TotalCount)> FetchDeadLettersPagedAsync(
        int page, int pageSize, string? typeFilter, CancellationToken ct)
        => Task.FromResult<(IReadOnlyList<OutboxMessage>, int)>((Array.Empty<OutboxMessage>(), 0));

    public Task<IReadOnlyList<OutboxMessage>> FetchDeadLettersAsync(int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

    public Task<OutboxMessage?> FindByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_messages.FirstOrDefault(m => m.Id == id));

    public Task<int> GetPendingCountAsync(CancellationToken ct)
        => Task.FromResult(_messages.Count(m => m.Status == "pending"));

    public Task<int> GetDeadLetterCountAsync(CancellationToken ct)
        => Task.FromResult(_messages.Count(m => m.Status == "dead_letter"));

    public Task<TimeSpan?> GetOldestPendingAgeAsync(DateTime nowUtc, CancellationToken ct)
        => Task.FromResult<TimeSpan?>(null);

    public Task<bool> TryReplayDeadLetterAsync(Guid id, Guid adminUserId, string reason, CancellationToken ct)
        => Task.FromResult(false);

    public Task<int> PurgeDoneOlderThanAsync(DateTime cutoffUtc, CancellationToken ct)
        => Task.FromResult(0);
}
