using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Payments;

// A browser redirect is only navigation. Payment state is read by the authenticated app.
[ApiController]
[AllowAnonymous]
[Route("api/v1/payments/payos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayosBrowserReturnController : ControllerBase
{
    [HttpGet("return")]
    public IActionResult Return([FromQuery] string? paymentId) => Landing("Đã quay về từ payOS", "Mở Fitmate để kiểm tra trạng thái thanh toán. Không dùng trang này làm xác nhận đã trả tiền.", paymentId);

    [HttpGet("cancel")]
    public IActionResult Cancel([FromQuery] string? paymentId) => Landing("Đã rời trang thanh toán", "Mở Fitmate để kiểm tra trạng thái đơn. Nếu tiền đã chuyển, ứng dụng sẽ đối chiếu với thông báo từ payOS.", paymentId);

    private ContentResult Landing(string title, string message, string? paymentId)
    {
        // A browser-supplied id is only a navigation hint; the app re-authorizes the payment.
        var appUrl = Guid.TryParse(paymentId, out var parsedId)
            ? "fitmate:///booking/payment-return?paymentId=" + parsedId.ToString("D")
            : "fitmate:///booking/my-appointments";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return new ContentResult
        {
            ContentType = "text/html; charset=utf-8",
            Content = $$"""
            <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Fitmate</title><style>body{font-family:system-ui,sans-serif;background:#f4faf7;color:#102820;margin:0;display:grid;min-height:100vh;place-items:center}main{max-width:440px;padding:32px;text-align:center}a{display:inline-block;background:#10b981;color:#fff;padding:14px 24px;border-radius:12px;text-decoration:none;font-weight:700}</style></head>
            <body><main><h1>{{title}}</h1><p>{{message}}</p><p><a href="{{appUrl}}">Mở ứng dụng Fitmate</a></p><p>Nếu nút không mở được ứng dụng, hãy quay lại Fitmate bằng tay.</p></main></body></html>
            """
        };
    }
}
