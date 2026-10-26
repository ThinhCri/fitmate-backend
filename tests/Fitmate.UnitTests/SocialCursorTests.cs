using System.Text;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class SocialCursorTests
{
    [Fact]
    public void Cursor_RoundTripsSubMillisecondPrecision()
    {
        var timestamp = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var id = Guid.NewGuid();
        var decoded = CursorEncoder.Decode(CursorEncoder.Encode(timestamp, id));
        decoded.createdAt.Should().Be(timestamp); decoded.id.Should().Be(id);
    }

    [Fact]
    public void Cursor_AcceptsLegacyMilliseconds()
    {
        var id = Guid.NewGuid(); const long ms = 1792972800123;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ms}|{id}"));
        CursorEncoder.Decode(encoded).Should().Be((DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, id));
    }

    [Theory]
    [InlineData("v2:9223372036854775807")]
    [InlineData("9223372036854775807")]
    [InlineData("v2:-1")]
    public void Cursor_RejectsTimestampOutsideRange(string timestamp)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{timestamp}|{Guid.NewGuid()}"));
        var act = () => CursorEncoder.Decode(encoded);
        act.Should().Throw<ValidationException>();
    }
}
