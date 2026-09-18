using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Errors;

/// <summary>
/// Factory tạo <see cref="ProblemDetails"/> chuẩn RFC 7807 cho toàn bộ API.
/// Đảm bảo mọi response lỗi có đủ 5 field bắt buộc (theo api-contract.md §6):
/// <c>type</c>, <c>title</c>, <c>status</c>, <c>code</c>, <c>traceId</c>,
/// và optional <c>detail</c> + <c>errors</c>.
/// Pure logic: không phụ thuộc HttpContext, có thể unit test trực tiếp.
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>
    /// Tạo <see cref="ProblemDetails"/> cơ bản.
    /// </summary>
    /// <param name="status">HTTP status code (vd 400, 404, 500).</param>
    /// <param name="title">Tiêu đề ngắn gọn (vd "Validation failed").</param>
    /// <param name="code">Mã ổn định từ <see cref="ErrorCodes"/>.</param>
    /// <param name="typeSlug">Slug cho docs URL, ghép vào base URI.</param>
    /// <param name="detail">Mô tả chi tiết (optional).</param>
    /// <param name="traceId">Trace id từ <c>HttpContext.TraceIdentifier</c> hoặc Activity.</param>
    /// <param name="errors">Map field → messages cho validation (optional).</param>
    public static ProblemDetails Create(
        int status,
        string title,
        string code,
        string typeSlug,
        string? detail = null,
        string? traceId = null,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://docs.fitmate.vn/errors/{typeSlug}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = traceId
        };
        problem.Extensions["code"] = code;
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }
        return problem;
    }

    /// <summary>
    /// Lấy trace id từ <see cref="HttpContext"/> hoặc <see cref="Activity.Current"/>.
    /// Trả về GUID string; fallback là <c>"unknown"</c> khi cả hai đều null.
    /// </summary>
    public static string ResolveTraceId(HttpContext? context)
    {
        if (context is not null && !string.IsNullOrEmpty(context.TraceIdentifier))
        {
            return context.TraceIdentifier;
        }
        return Activity.Current?.Id ?? "unknown";
    }
}
