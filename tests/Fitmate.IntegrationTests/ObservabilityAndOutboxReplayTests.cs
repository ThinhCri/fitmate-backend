using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

/// <summary>
/// BE-824: Integration tests cho Observability, Prometheus metrics endpoint, và Outbox Dead-Letter Replay
/// chạy trên PostgreSQL thật qua FitmateTestAppFactory.
/// Kiểm chứng chống replay trùng lặp đồng thời và bảo toàn AuditLog.
/// </summary>
public class ObservabilityAndOutboxReplayTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public ObservabilityAndOutboxReplayTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Correlation_And_TraceId_Headers_Are_Returned()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "corr-test-int-001");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-ID");
        response.Headers.GetValues("X-Correlation-ID").First().Should().Be("corr-test-int-001");
        response.Headers.Should().ContainKey("X-Trace-ID");
    }

    [Fact]
    public async Task AdminMetrics_Summary_And_Prometheus_Protected_By_AdminPolicy()
    {
        // 1. Unauthenticated or Member role -> 401 / 403
        var memberClient = _factory.CreateAuthenticatedClient(role: "Member");
        var forbiddenSummary = await memberClient.GetAsync("/api/v1/admin/metrics/summary");
        forbiddenSummary.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var forbiddenPrometheus = await memberClient.GetAsync("/api/v1/admin/metrics/prometheus");
        forbiddenPrometheus.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Admin role -> 200 OK
        var adminClient = _factory.CreateAuthenticatedClient(role: "Admin");
        var okSummary = await adminClient.GetAsync("/api/v1/admin/metrics/summary");
        okSummary.StatusCode.Should().Be(HttpStatusCode.OK);

        var summaryDoc = await okSummary.Content.ReadFromJsonAsync<JsonDocument>();
        summaryDoc.Should().NotBeNull();
        summaryDoc!.RootElement.GetProperty("service").GetString().Should().Be("Fitmate.Api");
        summaryDoc.RootElement.GetProperty("outbox").TryGetProperty("pending_count", out _).Should().BeTrue();

        var okPrometheus = await adminClient.GetAsync("/api/v1/admin/metrics/prometheus");
        okPrometheus.StatusCode.Should().Be(HttpStatusCode.OK);
        var prometheusText = await okPrometheus.Content.ReadAsStringAsync();
        prometheusText.Should().Contain("fitmate_http_requests_total");
        prometheusText.Should().Contain("fitmate_outbox_queue_depth");
    }

    [Fact]
    public async Task AdminOutbox_Replay_TransitionsToPending_PreservesLastError_And_RecordsAuditLog()
    {
        var adminClient = _factory.CreateAuthenticatedClient(role: "Admin");

        // 1. Chèn trực tiếp một message dead_letter vào PostgreSQL
        var messageId = Guid.NewGuid();
        var originalError = "Simulated provider timeout during push notification";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var msg = OutboxMessage.Enqueue("notification.push_requested", "{\"notificationId\":\"notif-1\"}");
            msg.MarkDeadLetter(originalError);

            // Gán Id xác định qua Reflection vì Id private set
            typeof(OutboxMessage).GetProperty("Id")!.SetValue(msg, messageId);

            await db.OutboxMessages.AddAsync(msg);
            await db.SaveChangesAsync();
        }

        // 2. Admin gọi Replay kèm reason
        var replayResponse = await adminClient.PostAsJsonAsync($"/api/v1/admin/outbox/dead-letter/{messageId}/replay", new
        {
            reason = "Investigated and FCM connectivity restored"
        });

        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Kiểm tra trạng thái trong PostgreSQL thật
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var reloadedMsg = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId);
            reloadedMsg.Should().NotBeNull();
            reloadedMsg!.Status.Should().Be("pending");
            reloadedMsg.Attempts.Should().Be(0);
            reloadedMsg.LastError.Should().Be(originalError, "Original LastError must be preserved for diagnostic integrity");

            // 4. Kiểm tra bản ghi AuditLog bền vững
            var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.TargetId == messageId && a.Action == "outbox.replayed");
            audit.Should().NotBeNull();
            audit!.Action.Should().Be("outbox.replayed");
            audit.Reason.Should().Be("Investigated and FCM connectivity restored");
        }
    }

    [Fact]
    public async Task AdminOutbox_ConcurrentReplay_Protection_OnlyOneSucceeds()
    {
        var adminClient1 = _factory.CreateAuthenticatedClient(role: "Admin");
        var adminClient2 = _factory.CreateAuthenticatedClient(role: "Admin");

        // 1. Chèn message dead_letter vào PostgreSQL
        var messageId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var msg = OutboxMessage.Enqueue("booking.confirmed", "{\"bookingId\":\"b-race-1\"}");
            msg.MarkDeadLetter("Race test failure");
            typeof(OutboxMessage).GetProperty("Id")!.SetValue(msg, messageId);

            await db.OutboxMessages.AddAsync(msg);
            await db.SaveChangesAsync();
        }

        // 2. Đồng thời gửi 2 request replay
        var task1 = adminClient1.PostAsJsonAsync($"/api/v1/admin/outbox/dead-letter/{messageId}/replay", new
        {
            reason = "Admin 1 triggered replay"
        });

        var task2 = adminClient2.PostAsJsonAsync($"/api/v1/admin/outbox/dead-letter/{messageId}/replay", new
        {
            reason = "Admin 2 triggered replay simultaneously"
        });

        var responses = await Task.WhenAll(task1, task2);

        // 3. Đúng 1 request thành công 200, request kia nhận 409 Conflict
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Exactly one concurrent replay must succeed");
        conflictCount.Should().Be(1, "Second concurrent replay must receive 409 Conflict");
    }
}

