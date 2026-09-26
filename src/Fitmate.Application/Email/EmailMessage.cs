namespace Fitmate.Application.Email;

/// <summary>
/// DTO chứa nội dung email đã render xong, sẵn sàng gửi qua SMTP adapter.
/// </summary>
public sealed record EmailMessage(
    string ToEmail,
    string Subject,
    string HtmlBody,
    string PlainTextBody);
