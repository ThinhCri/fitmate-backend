namespace Fitmate.Domain.Common;

public enum UserStatus
{
    Active = 0,
    Locked = 1,
    PendingDeletion = 2
}

public enum UserRole
{
    Member = 0,
    Pt = 1,
    Admin = 2
}

public enum CertificateStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Revoked = 3
}

public enum BookingStatus
{
    Held = 0,
    AwaitingPayment = 1,
    Paid = 2,
    Completed = 3,
    Cancelled = 4,
    Refunded = 5,
    Expired = 6,
    Confirmed = 7,
    InProgress = 8
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Refunded = 3,
    PartialRefunded = 4
}

public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

[System.Text.Json.Serialization.JsonConverter(typeof(PaymentMethodJsonConverter))]
public enum PaymentMethod
{
    Vnpay = 0,
    Payos = 1
}

public sealed class PaymentMethodJsonConverter : System.Text.Json.Serialization.JsonStringEnumConverter<PaymentMethod>
{
    public PaymentMethodJsonConverter() : base(System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}

public enum AvailabilityStatus
{
    Open = 0,
    Held = 1,
    Booked = 2
}

public enum ReportTargetType
{
    Post = 0,
    Comment = 1,
    User = 2,
    Certificate = 3
}

public enum ReportStatus
{
    Pending = 0,
    Resolved = 1,
    Dismissed = 2
}

public enum FriendRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum ReactionType
{
    Heart = 0,
    Flame = 1,
    Smile = 2
}

public enum PostVisibility
{
    Public = 0,
    Friends = 1,
    Private = 2
}

public enum ChatMessageSender
{
    User = 0,
    Ai = 1,
    Peer = 2
}

public enum MealSource
{
    Manual = 0,
    Dataset = 1,
    AiEstimate = 2
}

public enum NotificationType
{
    FriendRequest = 0,
    System = 1,
    Reminder = 2,
    Booking = 3,
    Payment = 4
}

public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2
}

public enum WorkoutSessionStatus
{
    Completed = 0,
    EndedEarly = 1,
    Skipped = 2
}

public enum ExerciseType
{
    Reps = 0,
    Time = 1
}

public enum WorkoutSetStatus
{
    Completed = 1,
    Partial = 2,
    Skipped = 3
}

public enum Gender
{
    Male = 0,
    Female = 1,
    Other = 2
}
