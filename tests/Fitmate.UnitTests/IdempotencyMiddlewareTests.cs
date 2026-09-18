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
        var rec = store.Items["valid-key-1234"];
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
}
