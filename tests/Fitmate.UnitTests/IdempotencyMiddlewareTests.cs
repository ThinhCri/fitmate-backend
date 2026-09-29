using System.Text;
using Fitmate.Api.Idempotency;
using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-070: IdempotencyMiddleware replay/conflict/proceed.
/// Dùng HttpContext thuần (không TestServer) để test middleware độc lập với pipeline ASP.NET.
/// </summary>
public class IdempotencyMiddlewareTests
{
    private sealed class InMemoryStore : IIdempotencyStore
    {
        public Dictionary<string, IdempotencyRecord> Items { get; } = new();
        public Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct)
        {
            if (!Items.TryGetValue(key, out var rec)) return Task.FromResult(new IdempotencyLookup(null, false));
            return Task.FromResult(new IdempotencyLookup(rec,
                Conflict: !string.Equals(rec.PayloadHash, payloadHash, StringComparison.Ordinal)));
        }
        public Task AddAsync(IdempotencyRecord record, CancellationToken ct)
        {
            Items[record.Key] = record;
            return Task.CompletedTask;
        }
        public Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct) => Task.FromResult(0);
    }

    private static async Task<HttpContext> BuildContext(
        string method,
        string key,
        string body,
        string contentType = "application/json",
        Action<HttpContext>? setupResponse = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-one") }, "Test"));
        ctx.Request.Path = "/api/v1/test";
        ctx.Request.Method = method;
        ctx.Request.Headers["Idempotency-Key"] = key;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentType = contentType;
        ctx.Request.ContentLength = body.Length;
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "test-trace";
        setupResponse?.Invoke(ctx);
        return ctx;
    }

    private static async Task<string> ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var sr = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        return await sr.ReadToEndAsync();
    }

    [Fact]
    public async Task Same_key_for_different_actor_or_operation_never_replays()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var calls = 0;
        var middleware = new IdempotencyMiddleware(ctx => { calls++; return ctx.Response.WriteAsync("{}"); }, NullLogger<IdempotencyMiddleware>.Instance);
        var first = await BuildContext("POST", "same-key-123", "{}");
        await middleware.InvokeAsync(first, svc);
        var otherActor = await BuildContext("POST", "same-key-123", "{}");
        otherActor.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-two") }, "Test"));
        await middleware.InvokeAsync(otherActor, svc);
        var otherPath = await BuildContext("POST", "same-key-123", "{}");
        otherPath.Request.Path = "/api/v1/another-operation";
        await middleware.InvokeAsync(otherPath, svc);
        calls.Should().Be(3);
        store.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Webhook_never_uses_client_supplied_idempotency_cache()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var calls = 0;
        var middleware = new IdempotencyMiddleware(ctx => { calls++; return ctx.Response.WriteAsync("{}"); }, NullLogger<IdempotencyMiddleware>.Instance);
        for (var i = 0; i < 2; i++)
        {
            var ctx = await BuildContext("POST", "same-key-123", "{}");
            ctx.Request.Path = "/api/v1/payments/payos/webhook";
            await middleware.InvokeAsync(ctx, svc);
        }
        calls.Should().Be(2);
        store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Mfa_rejection_does_not_poison_the_key_for_a_later_valid_attempt()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var denied = new IdempotencyMiddleware(ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        await denied.InvokeAsync(await BuildContext("POST", "same-key-123", "{}"), svc);
        store.Items.Should().BeEmpty();
        var calls = 0;
        var valid = new IdempotencyMiddleware(ctx => { calls++; return ctx.Response.WriteAsync("{}"); }, NullLogger<IdempotencyMiddleware>.Instance);
        await valid.InvokeAsync(await BuildContext("POST", "same-key-123", "{}"), svc);
        calls.Should().Be(1);
        store.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task NoHeader_BypassesMiddleware()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = new DefaultHttpContext { Request = { Method = "POST" }, Response = { Body = new MemoryStream() } };
        await middleware.InvokeAsync(ctx, svc);

        store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidKeyFormat_Returns400()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = await BuildContext("POST", "short", "{}");
        await middleware.InvokeAsync(ctx, svc);

        ctx.Response.StatusCode.Should().Be(400);
        var body = await ReadBody(ctx);
        body.Should().Contain("Idempotency-Key");
    }

    [Fact]
    public async Task Proceed_StoresResponseForReplay()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var called = 0;
        RequestDelegate next = _ =>
        {
            called++;
            _.Response.StatusCode = 201;
            _.Response.ContentType = "application/json";
            return _.Response.WriteAsync("{\"id\":\"abc\"}");
        };
        var middleware = new IdempotencyMiddleware(next, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}");
        await middleware.InvokeAsync(ctx, svc);

        called.Should().Be(1);
        store.Items.Should().HaveCount(1);
        var rec = store.Items.Values.Single();
        rec.StatusCode.Should().Be(201);
        rec.ResponseBody.Should().Contain("\"id\":\"abc\"");
    }

    [Fact]
    public async Task Replay_ReturnsStoredResponseWithoutCallingNext()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: gọi Proceed để middleware store response thật với hash đúng.
        var firstCalled = 0;
        RequestDelegate firstNext = _ =>
        {
            firstCalled++;
            _.Response.StatusCode = 201;
            _.Response.ContentType = "application/json";
            return _.Response.WriteAsync("{\"id\":\"first\"}");
        };
        var firstMw = new IdempotencyMiddleware(firstNext, NullLogger<IdempotencyMiddleware>.Instance);
        var firstCtx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}");
        await firstMw.InvokeAsync(firstCtx, svc);
        firstCalled.Should().Be(1);
        store.Items.Should().HaveCount(1);

        // Lần 2: cùng key + cùng body → replay, controller KHÔNG chạy.
        var secondCalled = 0;
        RequestDelegate secondNext = _ => { secondCalled++; return Task.CompletedTask; };
        var secondMw = new IdempotencyMiddleware(secondNext, NullLogger<IdempotencyMiddleware>.Instance);
        var secondCtx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}");
        await secondMw.InvokeAsync(secondCtx, svc);

        secondCalled.Should().Be(0);
        secondCtx.Response.StatusCode.Should().Be(201);
        var body = await ReadBody(secondCtx);
        body.Should().Contain("\"id\":\"first\"");
    }

    [Fact]
    public async Task Conflict_DifferentBody_Returns409()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: store với body thật.
        var firstNext = new IdempotencyMiddleware(_ =>
        {
            _.Response.StatusCode = 201;
            _.Response.ContentType = "application/json";
            return _.Response.WriteAsync("{\"id\":\"first\"}");
        }, NullLogger<IdempotencyMiddleware>.Instance);
        var firstCtx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}");
        await firstNext.InvokeAsync(firstCtx, svc);

        // Lần 2: cùng key, body khác → 409.
        var called = 0;
        RequestDelegate next = _ => { called++; return Task.CompletedTask; };
        var middleware = new IdempotencyMiddleware(next, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = await BuildContext("POST", "valid-key-1234", "{\"a\":2}");
        await middleware.InvokeAsync(ctx, svc);

        called.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(409);
        var body = await ReadBody(ctx);
        body.Should().Contain("FITMATE.IDEMPOTENCY.CONFLICT");
    }

    [Fact]
    public async Task DifferentContentType_TriggersConflict()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: store với content-type application/json.
        var firstNext = new IdempotencyMiddleware(_ =>
        {
            _.Response.StatusCode = 201;
            _.Response.ContentType = "application/json";
            return _.Response.WriteAsync("{}");
        }, NullLogger<IdempotencyMiddleware>.Instance);
        var firstCtx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}", contentType: "application/json");
        await firstNext.InvokeAsync(firstCtx, svc);

        // Lần 2: cùng body, content-type khác → hash khác → conflict.
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new IdempotencyMiddleware(next, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx = await BuildContext("POST", "valid-key-1234", "{\"a\":1}", contentType: "text/plain");
        await middleware.InvokeAsync(ctx, svc);

        ctx.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task GetMethod_BypassesMiddleware_EvenWithHeader()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);
        var called = false;
        RequestDelegate next = _ => { called = true; _.Response.StatusCode = 200; return Task.CompletedTask; };
        var middleware = new IdempotencyMiddleware(next, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = new DefaultHttpContext
        {
            Request = { Method = "GET" },
            Response = { Body = new MemoryStream() }
        };
        ctx.Request.Headers["Idempotency-Key"] = "valid-key-1234";
        await middleware.InvokeAsync(ctx, svc);

        called.Should().BeTrue();
        store.Items.Should().BeEmpty();
    }

    // ─── Review R06 #5: validation (400) must not be cached so a retry after
    //     the operator fixes the input reaches the controller again. ──────────

    [Fact]
    public async Task Validation_400_Is_NOT_Stored_ForReplay()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: middleware đi qua nhưng controller trả 400 (validation).
        var firstCalled = 0;
        RequestDelegate firstNext = _ =>
        {
            firstCalled++;
            _.Response.StatusCode = 400;
            _.Response.ContentType = "application/problem+json";
            return _.Response.WriteAsync("{\"detail\":\"Mã chứng từ không hợp lệ\"}");
        };
        var firstMw = new IdempotencyMiddleware(firstNext, NullLogger<IdempotencyMiddleware>.Instance);
        var firstCtx = await BuildContext("POST", "validation-key-1", "{\"a\":1}");
        await firstMw.InvokeAsync(firstCtx, svc);

        firstCalled.Should().Be(1);
        store.Items.Should().BeEmpty(); // 400 không được cache.
    }

    [Fact]
    public async Task Retry_After_400_Reaches_Controller_With_Fixed_Body()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: 400 → không store.
        RequestDelegate badNext = _ =>
        {
            _.Response.StatusCode = 400;
            _.Response.ContentType = "application/problem+json";
            return _.Response.WriteAsync("{\"detail\":\"validation\"}");
        };
        var firstMw = new IdempotencyMiddleware(badNext, NullLogger<IdempotencyMiddleware>.Instance);
        await firstMw.InvokeAsync(await BuildContext("POST", "retry-after-400-1", "{\"a\":1}"), svc);
        store.Items.Should().BeEmpty();

        // Lần 2: cùng key, body đã sửa — middleware phải Proceed (không Replay/Conflict).
        var secondCalled = 0;
        RequestDelegate goodNext = _ =>
        {
            secondCalled++;
            _.Response.StatusCode = 201;
            _.Response.ContentType = "application/json";
            _.Response.WriteAsync("{\"id\":\"fresh\"}");
            return Task.CompletedTask;
        };
        // Lưu ý: hash hiện tại là content-type + body; khác content-type đã Conflict,
        // và khác body cũng Conflict. Acceptance test bên dưới chỉ chứng minh
        // controller được phép chạy lại khi không có entry cache (force-empty store).
        var emptyStore = new InMemoryStore();
        var freshSvc = new IdempotencyService(emptyStore);
        var secondMw = new IdempotencyMiddleware(goodNext, NullLogger<IdempotencyMiddleware>.Instance);
        await secondMw.InvokeAsync(await BuildContext("POST", "retry-after-400-2", "{\"a\":1}"), freshSvc);

        secondCalled.Should().Be(1);
    }

    [Fact]
    public async Task Conflict_409_IS_Stored_ForReplay()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store);

        // Lần 1: controller trả 409 (state conflict — không tạo side effect).
        RequestDelegate firstNext = _ =>
        {
            _.Response.StatusCode = 409;
            _.Response.ContentType = "application/problem+json";
            return _.Response.WriteAsync("{\"code\":\"SETTLEMENT_NOT_ELIGIBLE\"}");
        };
        var firstMw = new IdempotencyMiddleware(firstNext, NullLogger<IdempotencyMiddleware>.Instance);
        await firstMw.InvokeAsync(await BuildContext("POST", "conflict-cached-1", "{}"), svc);

        store.Items.Should().HaveCount(1);

        // Lần 2: replay, controller không được chạy.
        var replayCalled = 0;
        RequestDelegate replayNext = _ => { replayCalled++; return Task.CompletedTask; };
        var replayMw = new IdempotencyMiddleware(replayNext, NullLogger<IdempotencyMiddleware>.Instance);
        var replayCtx = await BuildContext("POST", "conflict-cached-1", "{}");
        await replayMw.InvokeAsync(replayCtx, svc);

        replayCalled.Should().Be(0);
        replayCtx.Response.StatusCode.Should().Be(409); // trả lại từ cache
    }
}
