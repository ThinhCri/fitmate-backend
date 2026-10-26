using System.Text.Json;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class RevenueContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void TrainerRevenueOverview_Serializes_WithExactExpectedSnakeCaseKeys()
    {
        var overview = new TrainerRevenueOverview(
            Range: "30d",
            TotalGrossVnd: 5000000,
            PlatformFeeVnd: 500000,
            NetEarningsVnd: 4500000,
            PendingPayoutVnd: 2000000,
            CompletedPayoutVnd: 2500000,
            CompletedSessionsCount: 10,
            Chart: new List<TrainerRevenueChartPoint>
            {
                new(new DateOnly(2026, 10, 1), 2, 900000),
                new(new DateOnly(2026, 10, 2), 3, 1350000)
            });

        var json = JsonSerializer.Serialize(overview);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "range",
            "total_gross_vnd",
            "platform_fee_vnd",
            "net_earnings_vnd",
            "pending_payout_vnd",
            "completed_payout_vnd",
            "completed_sessions_count",
            "refund_adjustment_vnd",
            "chart"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);

        var chart = root.GetProperty("chart");
        chart.GetArrayLength().Should().Be(2);
        var firstPoint = chart[0];
        firstPoint.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "date", "sessions_count", "net_earnings_vnd" });
        firstPoint.GetProperty("date").GetString().Should().Be("2026-10-01");
        firstPoint.GetProperty("sessions_count").GetInt32().Should().Be(2);
        firstPoint.GetProperty("net_earnings_vnd").GetInt64().Should().Be(900000);

        root.TryGetProperty("totalGrossVnd", out _).Should().BeFalse();
        root.TryGetProperty("platformFeeVnd", out _).Should().BeFalse();
        root.TryGetProperty("netEarningsVnd", out _).Should().BeFalse();
        root.TryGetProperty("pendingPayoutVnd", out _).Should().BeFalse();
        root.TryGetProperty("completedPayoutVnd", out _).Should().BeFalse();
        root.TryGetProperty("completedSessionsCount", out _).Should().BeFalse();
    }

    [Fact]
    public void TrainerRevenueItem_Serializes_WithExactExpectedSnakeCaseKeys_AndEnumAsString()
    {
        var apptId = Guid.NewGuid();
        var item = new TrainerRevenueItem(
            AppointmentId: apptId,
            Date: new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc),
            GrossVnd: 500000,
            PlatformFeeVnd: 50000,
            NetVnd: 450000,
            Status: AppointmentStatus.Completed,
            PaidOutVnd: 450000,
            SettlementStatus: "completed");

        var json = JsonSerializer.Serialize(item);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "appointment_id",
            "date",
            "gross_vnd",
            "platform_fee_vnd",
            "net_vnd",
            "status",
            "paid_out_vnd",
            "settlement_status",
            "transferred_at_utc",
            "bank_reference",
            "payee_bank_name",
            "payee_account_number_masked",
            "payee_account_name"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("appointment_id").GetGuid().Should().Be(apptId);
        root.GetProperty("status").GetString().Should().Be("Completed");

        root.TryGetProperty("appointmentId", out _).Should().BeFalse();
        root.TryGetProperty("grossVnd", out _).Should().BeFalse();
        root.TryGetProperty("platformFeeVnd", out _).Should().BeFalse();
        root.TryGetProperty("netVnd", out _).Should().BeFalse();
        root.TryGetProperty("paidOutVnd", out _).Should().BeFalse();
        root.TryGetProperty("settlementStatus", out _).Should().BeFalse();
    }

    [Fact]
    public void TrainerRevenueHistoryPage_Serializes_WithExactPaginationKeys()
    {
        var page = new TrainerRevenueHistoryPage(
            Items: new List<TrainerRevenueItem>(),
            Total: 42,
            Page: 1,
            PageSize: 20);

        var json = JsonSerializer.Serialize(page);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "items", "total", "page", "pageSize" });
    }

    [Fact]
    public void OpenApi_RevenueSchemas_Match_RuntimeContract()
    {
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);

        diagnostic.Errors.Should().BeEmpty();

        // 1. TrainerRevenueOverview
        doc.Components.Schemas.Should().ContainKey("TrainerRevenueOverview");
        var overviewSchema = doc.Components.Schemas["TrainerRevenueOverview"];
        var expectedOverviewProps = new[]
        {
            "range",
            "total_gross_vnd",
            "platform_fee_vnd",
            "net_earnings_vnd",
            "pending_payout_vnd",
            "completed_payout_vnd",
            "completed_sessions_count",
            "refund_adjustment_vnd",
            "chart"
        };
        overviewSchema.Properties.Keys.Should().BeEquivalentTo(expectedOverviewProps);

        // 2. TrainerRevenueChartPoint
        doc.Components.Schemas.Should().ContainKey("TrainerRevenueChartPoint");
        var chartPointSchema = doc.Components.Schemas["TrainerRevenueChartPoint"];
        chartPointSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "date", "sessions_count", "net_earnings_vnd" });

        // 3. TrainerRevenueHistoryPage
        doc.Components.Schemas.Should().ContainKey("TrainerRevenueHistoryPage");
        var historyPageSchema = doc.Components.Schemas["TrainerRevenueHistoryPage"];
        historyPageSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "items", "total", "page", "pageSize" });

        // 4. TrainerRevenueItem
        doc.Components.Schemas.Should().ContainKey("TrainerRevenueItem");
        var itemSchema = doc.Components.Schemas["TrainerRevenueItem"];
        var expectedItemProps = new[]
        {
            "appointment_id",
            "date",
            "gross_vnd",
            "platform_fee_vnd",
            "net_vnd",
            "status",
            "paid_out_vnd",
            "settlement_status",
            "transferred_at_utc",
            "bank_reference",
            "payee_bank_name",
            "payee_account_number_masked",
            "payee_account_name"
        };
        itemSchema.Properties.Keys.Should().BeEquivalentTo(expectedItemProps);
    }
}
