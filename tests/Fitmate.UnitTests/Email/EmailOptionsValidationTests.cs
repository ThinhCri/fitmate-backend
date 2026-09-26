using Fitmate.Application.Email;
using Xunit;

namespace Fitmate.UnitTests.Email;

public class EmailOptionsValidationTests
{
    [Fact]
    public void ValidateForProduction_WithValidConfig_DoesNotThrow()
    {
        var options = CreateValidOptions();
        options.ValidateForProduction(); // should not throw
    }

    [Fact]
    public void ValidateForProduction_MissingSmtpHost_Throws()
    {
        var options = CreateValidOptions();
        options.SmtpHost = "";
        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateForProduction());
        Assert.Contains("SmtpHost", ex.Message);
    }

    [Theory]
    [InlineData(SmtpTlsMode.None)]
    [InlineData(SmtpTlsMode.Auto)]
    public void ValidateForProduction_ForbiddenTlsModes_Throws(SmtpTlsMode mode)
    {
        var options = CreateValidOptions();
        options.TlsMode = mode;
        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateForProduction());
        Assert.Contains("TlsMode", ex.Message);
        Assert.Contains(mode.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(SmtpTlsMode.StartTls)]
    [InlineData(SmtpTlsMode.SslOnConnect)]
    public void ValidateForProduction_AllowedTlsModes_DoesNotThrow(SmtpTlsMode mode)
    {
        var options = CreateValidOptions();
        options.TlsMode = mode;
        options.ValidateForProduction(); // should not throw
    }

    [Fact]
    public void ValidateForProduction_MissingSenderEmail_Throws()
    {
        var options = CreateValidOptions();
        options.SenderEmail = "";
        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateForProduction());
        Assert.Contains("SenderEmail", ex.Message);
    }

    [Fact]
    public void ValidateForProduction_MissingFrontendBaseUrl_Throws()
    {
        var options = CreateValidOptions();
        options.FrontendBaseUrl = "";
        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateForProduction());
        Assert.Contains("FrontendBaseUrl", ex.Message);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public void ValidateForProduction_InvalidFrontendBaseUrl_Throws(string url)
    {
        var options = CreateValidOptions();
        options.FrontendBaseUrl = url;
        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateForProduction());
        Assert.Contains("FrontendBaseUrl", ex.Message);
    }

    private static EmailOptions CreateValidOptions() => new()
    {
        SmtpHost = "smtp.example.com",
        SmtpPort = 587,
        TlsMode = SmtpTlsMode.StartTls,
        SenderEmail = "noreply@fitmate.vn",
        SenderName = "Fitmate",
        FrontendBaseUrl = "https://fitmate.vn"
    };
}
