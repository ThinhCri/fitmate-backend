using Fitmate.Domain.Outbox;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-824: Unit tests cho Outbox Dead-Letter state transition và replay mechanism.
/// </summary>
public class OutboxDeadLetterTests
{
    [Fact]
    public void MarkFailed_IncrementsAttempts_StaysPendingUnderMaxAttempts()
    {
        var msg = OutboxMessage.Enqueue("booking.confirmed", "{}");

        msg.MarkFailed("Network timeout", TimeSpan.FromSeconds(5));

        msg.Status.Should().Be("pending");
        msg.Attempts.Should().Be(1);
        msg.LastError.Should().Be("Network timeout");
    }

    [Fact]
    public void MarkFailed_TransitionsToDeadLetter_WhenMaxAttemptsReached()
    {
        var msg = OutboxMessage.Enqueue("payment.sync", "{}");

        for (int i = 0; i < OutboxMessage.MaxAttempts; i++)
        {
            msg.MarkFailed($"Error attempt {i + 1}", TimeSpan.FromSeconds(5));
        }

        msg.Status.Should().Be("dead_letter");
        msg.Attempts.Should().Be(OutboxMessage.MaxAttempts);
        msg.LastError.Should().Be($"Error attempt {OutboxMessage.MaxAttempts}");
    }

    [Fact]
    public void MarkDeadLetter_DirectlySetsStatusToDeadLetter()
    {
        var msg = OutboxMessage.Enqueue("email.welcome", "{}");

        msg.MarkDeadLetter("Fatal schema incompatibility");

        msg.Status.Should().Be("dead_letter");
        msg.LastError.Should().Be("Fatal schema incompatibility");
    }

    [Fact]
    public void Replay_ResetsAttempts_SetsStatusToPending_AndPreservesLastError()
    {
        var msg = OutboxMessage.Enqueue("email.welcome", "{}");
        msg.MarkDeadLetter("Fatal error");

        msg.Replay();

        msg.Status.Should().Be("pending");
        msg.Attempts.Should().Be(0);
        msg.NextAttemptAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        msg.LastError.Should().Be("Fatal error", "Original error must be preserved for diagnostic tracing");
    }
}
