using Fitmate.Infrastructure.Payments.Payos;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests.Payments;

public sealed class PayosAccountNumberTests
{
    [Theory]
    [InlineData("V3CAS6802047639", "6802 0476 39", true)]
    [InlineData("6802047639", "6802 0476 39", true)]
    [InlineData("v3cas6802047639", "6802047639", true)]
    [InlineData("99999999", "6802 0476 39", false)]
    [InlineData("16802047639", "6802 0476 39", false)]
    [InlineData("", "6802 0476 39", false)]
    [InlineData(null, "6802 0476 39", false)]
    public void Matches_handles_virtual_account_prefixes_and_spaces_safely(string? incoming, string? configured, bool expected)
    {
        PayosAccountNumber.Matches(incoming, configured).Should().Be(expected);
    }
}
