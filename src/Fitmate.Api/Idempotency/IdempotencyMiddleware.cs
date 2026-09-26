using System.Security.Cryptography;
using System.Text;
using Fitmate.Api.Errors;
using Fitmate.Application.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Idempotency;

/// <summary>
/// Middleware validate + replay cho header <c>Idempotency-Key</c> theo DEC-011 + api-contract.md §11.
/// Chỉ áp dụng cho mutation (POST/PUT/PATCH/DELETE) mà có header. Endpoint GET/anonymous vẫn chạy bình thường.
/// </summary>
/// <remarks>
/// Middleware wrap response bằng <see cref="IdempotencyResponseBody"/> để capture status + body
/// sau khi Controller chạy xong, lưu vào <see cref="IIdempotencyService"/> để replay sau.
/// </remarks>
public class IdempotencyMiddleware
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.Ordinal)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyService service)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            context.Request.Path.StartsWithSegments("/api/v1/payments/payos/webhook") ||
            context.Request.Path.StartsWithSegments("/api/v1/payments/vnpay/ipn") ||
            !MutatingMethods.Contains(context.Request.Method) ||
            !context.Request.Headers.TryGetValue(HeaderName, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            // Không có key → không áp dụng.
            await _next(context);
            return;
        }

        var key = keyValues.ToString().Trim();
        if (!LooksLikeKey(key))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                title: "Invalid Idempotency-Key",
                code: ErrorCodes.GeneralBadRequest,
                typeSlug: "idempotency/invalid-key",
                detail: "Idempotency-Key must be 8-128 chars, [A-Za-z0-9_-] only.",
                traceId: FitmateProblemDetails.ResolveTraceId(context));
            return;
        }

        // An identical key from another actor or endpoint must never replay money or private data.
        var actor = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(actor)) { await _next(context); return; }
        key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{actor}\n{context.Request.Method}\n{context.Request.Path}\n{key}")));

        // Enable buffering để Controller đọc lại body sau khi middleware hash xong.
        context.Request.EnableBuffering();
        var bodyBytes = await ReadBodyAsync(context.Request);
        context.Request.Body.Position = 0;

        var payloadHash = ComputeHash(bodyBytes, context.Request.ContentType);

        var decision = await service.CheckAsync(key, payloadHash, context.RequestAborted);

        switch (decision.Kind)
        {
            case IdempotencyDecisionKind.Replay:
                _logger.LogInformation("Idempotency replay: key={Key}, status={Status}", key, decision.StatusCode);
                await WriteReplayAsync(context, decision.StatusCode, decision.ResponseBody ?? string.Empty);
                return;

            case IdempotencyDecisionKind.Conflict:
                _logger.LogWarning("Idempotency conflict: key={Key}", key);
                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    title: "Idempotency key conflict",
                    code: ErrorCodes.IdempotencyConflict,
                    typeSlug: "idempotency/conflict",
                    detail: "Idempotency-Key đã dùng cho request khác (payload hash khác).",
                    traceId: FitmateProblemDetails.ResolveTraceId(context));
                return;

            case IdempotencyDecisionKind.Proceed:
            default:
                // Capture response rồi store sau khi Controller xử lý xong.
                {
                    var originalBody = context.Response.Body;
                    await using var buffer = new MemoryStream();
                    context.Response.Body = buffer;

                    try
                    {
                        await _next(context);

                        buffer.Position = 0;
                        var responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                        buffer.Position = 0;
                        await buffer.CopyToAsync(originalBody);

                        // Chỉ store khi response là JSON 2xx/4xx (không retry với 5xx).
                        if (ShouldStore(context.Response.StatusCode))
                        {
                            try
                            {
                                await service.StoreAsync(key, payloadHash, context.Response.StatusCode, responseBody, context.RequestAborted);
                            }
                            catch (Exception ex)
                            {
                                // Store fail không block response — log + tiếp tục.
                                _logger.LogError(ex, "Idempotency store failed for key={Key}", key);
                            }
                        }
                    }
                    finally
                    {
                        context.Response.Body = originalBody;
                    }
                }
                return;
        }
    }

    private static bool LooksLikeKey(string key) =>
        key.Length >= 8 && key.Length <= 128 &&
        key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    private static async Task<byte[]> ReadBodyAsync(HttpRequest req)
    {
        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer, req.HttpContext.RequestAborted);
        return buffer.ToArray();
    }

    private static string ComputeHash(byte[] body, string? contentType)
    {
        // Hash cả content-type + body để chống client đổi Content-Type nhưng giữ body.
        var prefix = $"{(contentType ?? string.Empty)}\n";
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var combined = new byte[prefixBytes.Length + body.Length];
        Buffer.BlockCopy(prefixBytes, 0, combined, 0, prefixBytes.Length);
        Buffer.BlockCopy(body, 0, combined, prefixBytes.Length, body.Length);
        return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
    }

    private static bool ShouldStore(int statusCode) =>
        statusCode is >= 200 and < 500 and not 401 and not 403 and not 429;

    private static async Task WriteReplayAsync(HttpContext context, int statusCode, string body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(body);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string code,
        string typeSlug,
        string detail,
        string traceId)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = FitmateProblemDetails.Create(status, title, code, typeSlug, detail, traceId);
        await context.Response.WriteAsJsonAsync(problem);
    }
}
