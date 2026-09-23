using Fitmate.Api.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-824: Unit tests cho Observability (CorrelationIdMiddleware, LogRedactor, FitmateMetrics).
/// </summary>
public class ObservabilityTests
{
    [Fact]
    public void ResolveCorrelationId_UsesValidHeader_WhenProvided()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.CorrelationHeaderName] = "custom-trace-id-12345";

        var result = CorrelationIdMiddleware.ResolveCorrelationId(context);

        result.Should().Be("custom-trace-id-12345");
    }

    [Fact]
    public void ResolveCorrelationId_FallsBackToTraceIdHeader_WhenCorrelationHeaderMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Trace-ID"] = "trace-header-6789";

        var result = CorrelationIdMiddleware.ResolveCorrelationId(context);

        result.Should().Be("trace-header-6789");
    }

    [Fact]
    public void ResolveCorrelationId_GeneratesNewId_WhenHeaderMissingOrInvalid()
    {
        var context = new DefaultHttpContext();

        var result = CorrelationIdMiddleware.ResolveCorrelationId(context);

        result.Should().NotBeNullOrWhiteSpace();
        result.Length.Should().Be(32); // Guid "N" format
    }

    [Fact]
    public void ResolveCorrelationId_SanitizesUnsafeCharacters()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.CorrelationHeaderName] = "<script>alert(1)</script>";

        var result = CorrelationIdMiddleware.ResolveCorrelationId(context);

        result.Should().NotContain("<script>");
        result.Length.Should().Be(32);
    }

    [Fact]
    public void LogRedactor_RedactsPasswords_InJsonText()
    {
        var rawJson = "{\"email\": \"user@fitmate.vn\", \"password\": \"SecretPass123!\", \"role\": \"member\"}";

        var redacted = LogRedactor.RedactText(rawJson);

        redacted.Should().NotContain("SecretPass123!");
        redacted.Should().Contain("\"password\": \"[REDACTED]\"");
        redacted.Should().Contain("user@fitmate.vn");
    }

    [Fact]
    public void LogRedactor_RedactsBearerTokens()
    {
        var text = "Sending request with Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz.abc";

        var redacted = LogRedactor.RedactText(text);

        redacted.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz.abc");
        redacted.Should().Contain("Bearer [REDACTED]");
    }

    [Fact]
    public void LogRedactor_RedactsCreditCards()
    {
        var text = "Payment card number: 4111111111111234 processed successfully";

        var redacted = LogRedactor.RedactText(text);

        redacted.Should().NotContain("4111111111111234");
        redacted.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void LogRedactor_RedactsDictionarySensitiveKeys()
    {
        var dict = new Dictionary<string, object?>
        {
            ["userId"] = "12345",
            ["password"] = "PlainTextPassword",
            ["refreshToken"] = "secret-token-xyz",
            ["medicalHistory"] = "Asthma and allergy to peanuts",
            ["action"] = "login"
        };

        var redacted = LogRedactor.RedactDictionary(dict);

        redacted["password"].Should().Be("[REDACTED]");
        redacted["refreshToken"].Should().Be("[REDACTED]");
        redacted["medicalHistory"].Should().Be("[REDACTED]");
        redacted["action"].Should().Be("login");
    }

    [Fact]
    public void FitmateMetrics_RecordsWithoutException()
    {
        var act = () =>
        {
            FitmateMetrics.RecordHttpRequest("GET", "/api/v1/users/me", 200, 15.5);
            FitmateMetrics.RecordAuthFailure("invalid_credentials");
            FitmateMetrics.RecordOutboxProcessed("booking.confirmed", "done");
            FitmateMetrics.RecordOutboxDeadLetter("payment.webhook");
            FitmateMetrics.RecordRefundProcessed("completed");
            FitmateMetrics.UpdateOperationalGauges(5, 120.5, 2, 450.0, 10, 3600);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void FitmateMetrics_GeneratePrometheusText_ContainsAllSLIs()
    {
        FitmateMetrics.RecordHttpRequest("POST", "/api/v1/bookings/holds", 201, 25.0);
        FitmateMetrics.UpdateOperationalGauges(
            outboxQueueDepth: 12,
            outboxOldestAgeSeconds: 85.5,
            refundsPendingCount: 3,
            refundsOldestAgeSeconds: 3600.0,
            dbConnectionPoolActive: 8,
            backupFreshnessSeconds: 7200.0);

        var output = FitmateMetrics.GeneratePrometheusText();

        output.Should().Contain("fitmate_http_requests_total");
        output.Should().Contain("fitmate_outbox_queue_depth 12");
        output.Should().Contain("fitmate_outbox_oldest_age_seconds 85.50");
        output.Should().Contain("fitmate_refunds_pending_count 3");
        output.Should().Contain("fitmate_db_connection_pool_active 8");
        output.Should().Contain("fitmate_backup_freshness_seconds 7200.00");
    }

    [Fact]
    public void ResolveRouteTemplate_ReturnsUnmatched_WhenNoEndpoint()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/unknown-endpoint/12345";

        var result = CorrelationIdMiddleware.ResolveRouteTemplate(context);

        result.Should().Be("unmatched");
    }

    [Fact]
    public void ResolveRouteTemplate_ReturnsHealthPath_ForHealthProbes()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";

        var result = CorrelationIdMiddleware.ResolveRouteTemplate(context);

        result.Should().Be("/health/ready");
    }
}
