using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Fitmate.Api.Observability;

/// <summary>
/// BE-824: Middleware gắn Correlation ID và phân biệt rõ ràng với W3C Distributed TraceId.
/// Thu thập HTTP metrics với route template có chặn cardinality nghiêm ngặt (không lọt ID vào metric tags).
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationHeaderName = "X-Correlation-ID";
    public const string TraceHeaderName = "X-Trace-ID";
    private static readonly Regex SafeIdRegex = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. W3C Distributed TraceId: strictly from Activity.Current, fallback to context.TraceIdentifier
        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrEmpty(traceId) || traceId == "00000000000000000000000000000000")
        {
            traceId = context.TraceIdentifier;
        }

        // 2. CorrelationId: business correlation identifier (separate from traceId)
        var correlationId = ResolveCorrelationId(context);

        // Header propagation on response
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationHeaderName))
            {
                context.Response.Headers[CorrelationHeaderName] = correlationId;
            }
            if (!context.Response.Headers.ContainsKey(TraceHeaderName))
            {
                context.Response.Headers[TraceHeaderName] = traceId;
            }
            return Task.CompletedTask;
        });

        var sw = Stopwatch.StartNew();

        // Push separate CorrelationId and TraceId properties to logger scope
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = traceId,
            ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        }))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();
                // Cardinality Guard: extract route template (e.g. /api/v1/users/{id})
                var routeTemplate = ResolveRouteTemplate(context);
                if (!routeTemplate.StartsWith("/health/live", StringComparison.OrdinalIgnoreCase))
                {
                    FitmateMetrics.RecordHttpRequest(
                        context.Request.Method,
                        routeTemplate,
                        context.Response.StatusCode,
                        sw.Elapsed.TotalMilliseconds);
                }
            }
        }
    }

    /// <summary>
    /// Trích xuất route template tĩnh từ RouteEndpoint để bảo đảm không bùng nổ cardinality.
    /// Không bao giờ trả về chuỗi chứa User ID, Booking ID hay URL parameters thực tế.
    /// </summary>
    public static string ResolveRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern.RawText is not null)
        {
            var rawText = routeEndpoint.RoutePattern.RawText;
            return rawText.StartsWith('/') ? rawText : "/" + rawText;
        }

        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        if (path == "/" || path == "")
        {
            return "/";
        }

        return "unmatched";
    }

    public static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeaderName, out var headerValues) &&
            !string.IsNullOrWhiteSpace(headerValues))
        {
            var candidate = headerValues.ToString().Trim();
            if (SafeIdRegex.IsMatch(candidate))
            {
                return candidate;
            }
        }

        if (context.Request.Headers.TryGetValue(TraceHeaderName, out var traceValues) &&
            !string.IsNullOrWhiteSpace(traceValues))
        {
            var candidate = traceValues.ToString().Trim();
            if (SafeIdRegex.IsMatch(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
