using System.Text.RegularExpressions;
using Fitmate.Application.Storage;

namespace Fitmate.Api.Storage;

/// <summary>
/// Middleware that intercepts requests to <c>/uploads/...</c> and validates
/// the HMAC-SHA256 signature embedded in the query string.  If validation
/// fails or the URL has expired the request is rejected with 403 before the
/// static-file handler runs.
/// </summary>
public sealed class SignedUrlMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISignedUrlService _signedUrl;
    private readonly ILogger<SignedUrlMiddleware> _logger;
    private static readonly Regex UploadsPattern = new(@"^/uploads/", RegexOptions.IgnoreCase);

    public SignedUrlMiddleware(
        RequestDelegate next,
        ISignedUrlService signedUrl,
        ILogger<SignedUrlMiddleware> logger)
    {
        _next = next;
        _signedUrl = signedUrl;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (!UploadsPattern.IsMatch(path))
        {
            await _next(ctx);
            return;
        }

        // Combine path + query for validation.
        var raw = path + ctx.Request.QueryString;
        var relativePath = _signedUrl.TryValidateAndExtractPath(raw);

        if (relativePath is null)
        {
            // The query includes the signature; never persist it in application logs.
            _logger.LogWarning("Signed URL validation failed");
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.com/403",
                title = "Forbidden",
                status = 403,
                detail = "Invalid or expired signed URL."
            });
            return;
        }

        // Store the resolved path so the static-file handler can use it.
        ctx.Items["SignedUrlRelativePath"] = relativePath;
        await _next(ctx);
    }
}
