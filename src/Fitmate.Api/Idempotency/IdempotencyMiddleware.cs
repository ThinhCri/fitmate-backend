using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Api.Errors;
using Fitmate.Application.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Idempotency;

/// <summary>
/// Middleware validate + replay cho header <c>Idempotency-Key</c> theo DEC-011 + api-contract.md §11 + Plan v9.1.
/// Chỉ áp dụng cho mutation (POST/PUT/PATCH/DELETE) mà có header. Endpoint GET/anonymous vẫn chạy bình thường.
/// </summary>
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

        var clientKey = keyValues.ToString().Trim();
        if (!LooksLikeKey(clientKey))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                title: "Invalid Idempotency-Key",
                code: ErrorCodes.GeneralBadRequest,
                typeSlug: "idempotency/invalid-key",
                detail: "Idempotency-Key must be 8-128 chars, [A-Za-z0-9_-] only.",
                traceId: FitmateProblemDetails.ResolveTraceId(context));
            return;
        }

        var actor = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(actor))
        {
            await _next(context);
            return;
        }

        // StorageKey = SHA256(actor\nmethod\npath\nclientKey)
        var operation = context.Request.Path.Value ?? string.Empty;
        var keyRaw = $"{actor}\n{context.Request.Method}\n{operation}\n{clientKey}";
        var storageKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyRaw)));

        // Enable buffering để Controller đọc lại body sau khi middleware hash xong.
        context.Request.EnableBuffering();
        var bodyBytes = await ReadBodyAsync(context.Request);
        context.Request.Body.Position = 0;

        string payloadHash;
        try
        {
            payloadHash = CanonicalJsonHasher.ComputeHash(bodyBytes, context.Request.ContentType);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed JSON body in idempotent request key={Key}", clientKey);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                title: "Invalid JSON",
                code: ErrorCodes.GeneralBadRequest,
                typeSlug: "idempotency/invalid-json",
                detail: "Malformed JSON payload.",
                traceId: FitmateProblemDetails.ResolveTraceId(context));
            return;
        }

        var ownerToken = Guid.NewGuid();
        var isFinancial = IsFinancialPath(context.Request.Path);
        var operationName = $"{context.Request.Method} {operation}";
        if (operationName.Length > 128)
            operationName = $"{operationName[..119]}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationName)))[..8]}";

        var acquireResult = await service.AcquireAsync(
            storageKey,
            payloadHash,
            ownerToken,
            isFinancial,
            actor,
            operationName,
            context.RequestAborted);

        switch (acquireResult.Status)
        {
            case IdempotencyAcquireStatus.PayloadConflict:
                _logger.LogWarning("Idempotency conflict: key={Key}", clientKey);
                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    title: "Idempotency key conflict",
                    code: ErrorCodes.IdempotencyConflict,
                    typeSlug: "idempotency/conflict",
                    detail: "Idempotency-Key đã dùng cho request khác (payload hash khác).",
                    traceId: FitmateProblemDetails.ResolveTraceId(context));
                return;

            case IdempotencyAcquireStatus.InProgress:
                _logger.LogWarning("Idempotency in progress: key={Key}", clientKey);
                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    title: "Request in progress",
                    code: ErrorCodes.IdempotencyInProgress,
                    typeSlug: "idempotency/in-progress",
                    detail: "Yêu cầu với Idempotency-Key này đang được xử lý, vui lòng thử lại sau.",
                    traceId: FitmateProblemDetails.ResolveTraceId(context));
                return;

            case IdempotencyAcquireStatus.Replay:
                var rec = acquireResult.Record;
                var statusCode = rec?.StatusCode ?? StatusCodes.Status200OK;
                var responseBody = rec?.ResponseBody ?? string.Empty;
                _logger.LogInformation("Idempotency replay: key={Key}, status={Status}", clientKey, statusCode);
                await WriteReplayAsync(context, statusCode, responseBody);
                return;

            case IdempotencyAcquireStatus.Acquired:
            default:
            {
                var originalBody = context.Response.Body;
                await using var buffer = new MemoryStream();
                context.Response.Body = buffer;

                bool mutationSucceeded = false;
                try
                {
                    await _next(context);

                    mutationSucceeded = context.Response.StatusCode >= 200 && context.Response.StatusCode < 300
                                        || context.Response.StatusCode == StatusCodes.Status409Conflict;

                    buffer.Position = 0;
                    var capturedResponse = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                    buffer.Position = 0;
                    await buffer.CopyToAsync(originalBody);

                    if (mutationSucceeded)
                    {
                        var bodyToStore = ShouldStorePath(context.Request.Path) ? capturedResponse : null;
                        var completed = await service.CompleteAsync(storageKey, ownerToken, context.Response.StatusCode, bodyToStore, context.RequestAborted);
                        if (!completed && isFinancial)
                        {
                            _logger.LogError("Idempotency lease lost for financial mutation storageKey={Key}", storageKey);
                            throw new InvalidOperationException("Failed to complete financial idempotency record: lease ownership lost.");
                        }
                    }
                    else if (context.Response.StatusCode is StatusCodes.Status400BadRequest
                             or StatusCodes.Status401Unauthorized
                             or StatusCodes.Status403Forbidden
                             or StatusCodes.Status422UnprocessableEntity)
                    {
                        await service.ReleaseAsync(storageKey, ownerToken, context.RequestAborted);
                    }
                    else
                    {
                        await service.FailAsync(storageKey, ownerToken, context.RequestAborted);
                    }
                }
                catch
                {
                    if (!mutationSucceeded)
                    {
                        try
                        {
                            await service.FailAsync(storageKey, ownerToken, CancellationToken.None);
                        }
                        catch
                        {
                            // ignore secondary failure handling exceptions
                        }
                    }
                    throw;
                }
                finally
                {
                    context.Response.Body = originalBody;
                }
                return;
            }
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

    private static bool IsFinancialPath(PathString path)
    {
        var p = path.Value ?? string.Empty;
        return p.Contains("/payments/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/refunds/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/settlements/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/finance/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/session-packages/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldStorePath(PathString path)
    {
        var p = path.Value ?? string.Empty;
        if (p.Contains("/auth/", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/totp", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/recovery", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/step-up", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static async Task WriteReplayAsync(HttpContext context, int statusCode, string body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["X-Idempotent-Replay"] = "true";
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
