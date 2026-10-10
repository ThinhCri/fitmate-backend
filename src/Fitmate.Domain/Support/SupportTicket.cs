using Fitmate.Domain.Common;

namespace Fitmate.Domain.Support;

public enum BookingIncidentOutcome
{
    SessionDelivered = 1,
    MemberNoShow = 2,
    PtNoShow = 3
}

public enum SupportTicketStatus
{
    Open = 1,
    InProgress = 2,
    WaitingForUser = 3,
    Resolved = 4,
    Closed = 5
}

public enum SupportTicketPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4
}

/// <summary>
/// BE-819 / BR-15: Support ticket entity.
/// Manages user support requests and admin interactions with lifecycle tracking.
/// </summary>
public class SupportTicket
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BookingId { get; private set; }
    public void LinkBooking(Guid bookingId) => BookingId = bookingId;
    public string Subject { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public SupportTicketPriority Priority { get; private set; } = SupportTicketPriority.Normal;
    public SupportTicketStatus Status { get; private set; } = SupportTicketStatus.Open;

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public BookingIncidentOutcome? BookingResolution { get; private set; }
    public Guid? BookingResolvedByAdminId { get; private set; }
    public DateTime? BookingResolvedAtUtc { get; private set; }
    public string? BookingResolutionReason { get; private set; }
    public string? BookingEvidenceReference { get; private set; }

    private readonly List<SupportTicketMessage> _messages = new();
    public IReadOnlyCollection<SupportTicketMessage> Messages => _messages.AsReadOnly();

    private SupportTicket() { }

    public static SupportTicket Create(
        Guid userId,
        string subject,
        string category,
        SupportTicketPriority priority,
        string initialMessage,
        List<string>? attachmentUrls = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be empty.", nameof(subject));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category cannot be empty.", nameof(category));
        if (string.IsNullOrWhiteSpace(initialMessage))
            throw new ArgumentException("Initial message cannot be empty.", nameof(initialMessage));

        var now = DateTime.UtcNow;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Subject = subject.Trim(),
            Category = category.Trim().ToLowerInvariant(),
            Priority = priority,
            Status = SupportTicketStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        };

        ticket._messages.Add(new SupportTicketMessage(
            Guid.NewGuid(),
            ticket.Id,
            userId,
            isAdmin: false,
            initialMessage.Trim(),
            attachmentUrls ?? new List<string>(),
            now));

        return ticket;
    }

    public void AddUserReply(Guid userId, string content, List<string>? attachmentUrls = null)
    {
        if (Status == SupportTicketStatus.Closed)
            throw new InvalidOperationException("Cannot reply to a closed ticket.");

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Message content cannot be empty.", nameof(content));

        var now = DateTime.UtcNow;
        _messages.Add(new SupportTicketMessage(
            Guid.NewGuid(),
            Id,
            userId,
            isAdmin: false,
            content.Trim(),
            attachmentUrls ?? new List<string>(),
            now));

        Status = SupportTicketStatus.Open;
        UpdatedAt = now;
    }

    public void AddAdminReply(Guid adminUserId, string content, List<string>? attachmentUrls = null)
    {
        if (Status == SupportTicketStatus.Closed)
            throw new InvalidOperationException("Cannot reply to a closed ticket.");

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Message content cannot be empty.", nameof(content));

        var now = DateTime.UtcNow;
        _messages.Add(new SupportTicketMessage(
            Guid.NewGuid(),
            Id,
            adminUserId,
            isAdmin: true,
            content.Trim(),
            attachmentUrls ?? new List<string>(),
            now));

        Status = SupportTicketStatus.WaitingForUser;
        UpdatedAt = now;
    }

    public void ChangeStatus(SupportTicketStatus newStatus)
    {
        if (BookingId.HasValue && BookingResolution is null &&
            newStatus is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
            throw new ConflictException("BOOKING_DECISION_REQUIRED",
                "A linked booking incident needs an Admin decision before it can be closed.");
        var now = DateTime.UtcNow;
        Status = newStatus;
        UpdatedAt = now;

        if (newStatus == SupportTicketStatus.Resolved)
        {
            ResolvedAt = now;
        }
        else if (newStatus == SupportTicketStatus.Closed)
        {
            ClosedAt = now;
        }
    }

    public void CloseByUser(Guid userId)
    {
        if (UserId != userId)
            throw new InvalidOperationException("Only the ticket owner can close this ticket.");

        ChangeStatus(SupportTicketStatus.Closed);
    }

    public void ResolveBookingIncident(Guid adminId, BookingIncidentOutcome outcome,
        string reason, string evidenceReference, DateTime? nowUtc = null)
    {
        if (!BookingId.HasValue)
            throw new ConflictException("BOOKING_LINK_REQUIRED", "The ticket is not linked to a booking.");
        if (BookingResolution.HasValue || Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
            throw new ConflictException("BOOKING_ALREADY_RESOLVED", "The booking incident already has a decision.");
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 20 or > 1000)
            throw new ArgumentException("Decision reason must have 20-1000 characters.", nameof(reason));
        if (string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length is < 5 or > 200)
            throw new ArgumentException("Private evidence reference must have 5-200 characters.", nameof(evidenceReference));
        var now = nowUtc ?? DateTime.UtcNow;
        BookingResolution = outcome;
        BookingResolvedByAdminId = adminId;
        BookingResolvedAtUtc = now;
        BookingResolutionReason = reason.Trim();
        BookingEvidenceReference = evidenceReference.Trim();
        _messages.Add(new SupportTicketMessage(Guid.NewGuid(), Id, adminId, true,
            BookingResolutionReason, new List<string>(), now));
        Status = SupportTicketStatus.Resolved;
        ResolvedAt = now;
        UpdatedAt = now;
    }
}

public class SupportTicketMessage
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid SenderId { get; private set; }
    public bool IsAdmin { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public List<string> AttachmentUrls { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }

    private SupportTicketMessage() { }

    public SupportTicketMessage(
        Guid id,
        Guid ticketId,
        Guid senderId,
        bool isAdmin,
        string content,
        List<string> attachmentUrls,
        DateTime createdAt)
    {
        Id = id;
        TicketId = ticketId;
        SenderId = senderId;
        IsAdmin = isAdmin;
        Content = content;
        AttachmentUrls = attachmentUrls;
        CreatedAt = createdAt;
    }
}

