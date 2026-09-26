namespace Fitmate.Application.Outbox;

/// <summary>
/// Ngoại lệ đại diện cho lỗi vĩnh viễn (permanent/terminal failure) trong quá trình xử lý Outbox message.
/// Khi bắt được ngoại lệ này, OutboxWorker sẽ lập tức chuyển message sang trạng thái 'dead_letter'
/// thay vì retry vô ích.
/// </summary>
public class TerminalOutboxException : Exception
{
    public TerminalOutboxException(string message) : base(message)
    {
    }

    public TerminalOutboxException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
