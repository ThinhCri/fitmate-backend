using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace Fitmate.Api.Observability;

/// <summary>
/// BE-824: Quản lý metrics chuẩn hóa của Fitmate Backend theo System.Diagnostics.Metrics (.NET 8 native).
/// Đảm bảo tính tương thích OpenTelemetry/Prometheus và giới hạn nghiêm ngặt cardinality.
/// Cung cấp đầy đủ các chỉ số SLIs: Traffic, Latency, Errors, Outbox Queue Age, Refund Age, DB Pool, Backup Freshness.
/// </summary>
public static class FitmateMetrics
{
    public const string MeterName = "Fitmate.Api";
    public const string MeterVersion = "1.0.0";

    public static readonly Meter Meter = new(MeterName, MeterVersion);

    // 1. HTTP Traffic & Latency (tags: method, route, status_code)
    private static readonly Counter<long> HttpRequestsTotal = Meter.CreateCounter<long>(
        name: "fitmate_http_requests_total",
        unit: "requests",
        description: "Tổng số HTTP requests đã hoàn thành trên hệ thống");

    private static readonly Histogram<double> HttpRequestDurationMs = Meter.CreateHistogram<double>(
        name: "fitmate_http_request_duration_ms",
        unit: "ms",
        description: "Thời gian xử lý HTTP request tính bằng mili-giây");

    // 2. Authentication Failures (tags: reason)
    private static readonly Counter<long> AuthFailuresTotal = Meter.CreateCounter<long>(
        name: "fitmate_auth_failures_total",
        unit: "failures",
        description: "Tổng số lần xác thực thất bại");

    // 3. Outbox Operations & Age
    private static readonly Counter<long> OutboxProcessedTotal = Meter.CreateCounter<long>(
        name: "fitmate_outbox_processed_total",
        unit: "messages",
        description: "Tổng số outbox messages đã xử lý");

    private static readonly Counter<long> OutboxDeadLetterTotal = Meter.CreateCounter<long>(
        name: "fitmate_outbox_dead_letter_total",
        unit: "messages",
        description: "Tổng số outbox messages rơi vào dead-letter queue");

    // 4. Financial & Refunds (tags: status)
    private static readonly Counter<long> RefundsProcessedTotal = Meter.CreateCounter<long>(
        name: "fitmate_refunds_processed_total",
        unit: "refunds",
        description: "Tổng số yêu cầu hoàn tiền được xử lý");

    // In-memory snapshots for exporter / admin summary (thread-safe)
    private static readonly ConcurrentDictionary<string, long> RequestCounts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> AuthFailureCounts = new(StringComparer.Ordinal);
    private static long _outboxDeadLetters;
    private static long _outboxProcessed;
    private static long _refundsProcessed;

    // Operational Gauge Values (cập nhật từ background sweep / polling)
    private static long _outboxQueueDepth;
    private static double _outboxOldestAgeSeconds;
    private static long _refundsPendingCount;
    private static double _refundsOldestAgeSeconds;
    private static long _dbConnectionPoolActive;
    private static double _backupFreshnessSeconds;

    static FitmateMetrics()
    {
        // Observable Gauges theo chuẩn System.Diagnostics.Metrics
        Meter.CreateObservableGauge("fitmate_outbox_queue_depth", () => _outboxQueueDepth, "messages", "Số bản ghi outbox đang pending");
        Meter.CreateObservableGauge("fitmate_outbox_oldest_age_seconds", () => _outboxOldestAgeSeconds, "seconds", "Độ tuổi của bản ghi outbox pending lâu nhất");
        Meter.CreateObservableGauge("fitmate_refunds_pending_count", () => _refundsPendingCount, "refunds", "Số lượng yêu cầu hoàn tiền đang chờ duyệt");
        Meter.CreateObservableGauge("fitmate_refunds_oldest_age_seconds", () => _refundsOldestAgeSeconds, "seconds", "Độ tuổi của yêu cầu hoàn tiền pending lâu nhất");
        Meter.CreateObservableGauge("fitmate_db_connection_pool_active", () => _dbConnectionPoolActive, "connections", "Số kết nối DB pool đang active");
        Meter.CreateObservableGauge("fitmate_backup_freshness_seconds", () => _backupFreshnessSeconds, "seconds", "Thời gian tính từ bản sao lưu DB gần nhất");
    }

    public static void RecordHttpRequest(string method, string routeTemplate, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "method", method.ToUpperInvariant() },
            { "route", routeTemplate },
            { "status_code", statusCode.ToString() }
        };

        HttpRequestsTotal.Add(1, tags);
        HttpRequestDurationMs.Record(durationMs, tags);

        var key = $"{method.ToUpperInvariant()}_{routeTemplate}_{statusCode}";
        RequestCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
    }

    public static void RecordAuthFailure(string reason)
    {
        AuthFailuresTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));
        AuthFailureCounts.AddOrUpdate(reason, 1, (_, v) => v + 1);
    }

    public static void RecordOutboxProcessed(string type, string status)
    {
        OutboxProcessedTotal.Add(1, new TagList
        {
            { "type", type },
            { "status", status }
        });
        Interlocked.Increment(ref _outboxProcessed);
    }

    public static void RecordOutboxDeadLetter(string type)
    {
        OutboxDeadLetterTotal.Add(1, new KeyValuePair<string, object?>("type", type));
        Interlocked.Increment(ref _outboxDeadLetters);
    }

    public static void RecordRefundProcessed(string status)
    {
        RefundsProcessedTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        Interlocked.Increment(ref _refundsProcessed);
    }

    public static void UpdateOperationalGauges(
        long outboxQueueDepth,
        double outboxOldestAgeSeconds,
        long refundsPendingCount,
        double refundsOldestAgeSeconds,
        long dbConnectionPoolActive = 0,
        double backupFreshnessSeconds = 0)
    {
        Interlocked.Exchange(ref _outboxQueueDepth, outboxQueueDepth);
        _outboxOldestAgeSeconds = outboxOldestAgeSeconds;
        Interlocked.Exchange(ref _refundsPendingCount, refundsPendingCount);
        _refundsOldestAgeSeconds = refundsOldestAgeSeconds;
        Interlocked.Exchange(ref _dbConnectionPoolActive, dbConnectionPoolActive);
        _backupFreshnessSeconds = backupFreshnessSeconds;
    }

    /// <summary>
    /// Xuất text theo định dạng chuẩn Prometheus exposition format (text/plain 0.0.4)
    /// Hỗ trợ Prometheus / OpenTelemetry Collector cào dữ liệu qua HTTP.
    /// </summary>
    public static string GeneratePrometheusText()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# HELP fitmate_http_requests_total Total completed HTTP requests");
        sb.AppendLine("# TYPE fitmate_http_requests_total counter");
        foreach (var (k, count) in RequestCounts)
        {
            var parts = k.Split('_');
            if (parts.Length >= 3)
            {
                var method = parts[0];
                var status = parts[^1];
                var route = string.Join('_', parts.Skip(1).Take(parts.Length - 2));
                sb.AppendLine($"fitmate_http_requests_total{{method=\"{method}\",route=\"{route}\",status_code=\"{status}\"}} {count}");
            }
        }

        sb.AppendLine("# HELP fitmate_auth_failures_total Total authentication failures");
        sb.AppendLine("# TYPE fitmate_auth_failures_total counter");
        foreach (var (reason, count) in AuthFailureCounts)
        {
            sb.AppendLine($"fitmate_auth_failures_total{{reason=\"{reason}\"}} {count}");
        }

        sb.AppendLine("# HELP fitmate_outbox_queue_depth Pending outbox message count");
        sb.AppendLine("# TYPE fitmate_outbox_queue_depth gauge");
        sb.AppendLine($"fitmate_outbox_queue_depth {_outboxQueueDepth}");

        sb.AppendLine("# HELP fitmate_outbox_oldest_age_seconds Oldest pending outbox message age");
        sb.AppendLine("# TYPE fitmate_outbox_oldest_age_seconds gauge");
        sb.AppendLine($"fitmate_outbox_oldest_age_seconds {_outboxOldestAgeSeconds:F2}");

        sb.AppendLine("# HELP fitmate_outbox_dead_letter_total Total messages moved to dead-letter");
        sb.AppendLine("# TYPE fitmate_outbox_dead_letter_total counter");
        sb.AppendLine($"fitmate_outbox_dead_letter_total {_outboxDeadLetters}");

        sb.AppendLine("# HELP fitmate_refunds_pending_count Pending refund requests");
        sb.AppendLine("# TYPE fitmate_refunds_pending_count gauge");
        sb.AppendLine($"fitmate_refunds_pending_count {_refundsPendingCount}");

        sb.AppendLine("# HELP fitmate_refunds_oldest_age_seconds Oldest pending refund age in seconds");
        sb.AppendLine("# TYPE fitmate_refunds_oldest_age_seconds gauge");
        sb.AppendLine($"fitmate_refunds_oldest_age_seconds {_refundsOldestAgeSeconds:F2}");

        sb.AppendLine("# HELP fitmate_db_connection_pool_active Active PostgreSQL connections");
        sb.AppendLine("# TYPE fitmate_db_connection_pool_active gauge");
        sb.AppendLine($"fitmate_db_connection_pool_active {_dbConnectionPoolActive}");

        sb.AppendLine("# HELP fitmate_backup_freshness_seconds Seconds since last successful backup");
        sb.AppendLine("# TYPE fitmate_backup_freshness_seconds gauge");
        sb.AppendLine($"fitmate_backup_freshness_seconds {_backupFreshnessSeconds:F2}");

        return sb.ToString();
    }
}
