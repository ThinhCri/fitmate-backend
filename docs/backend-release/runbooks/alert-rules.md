# Quy định Cảnh báo Vận hành & Chỉ số SLIs (Alert Rules & SLIs)

> Định nghĩa các chỉ số đo lường dịch vụ (SLIs), ngưỡng kích hoạt cảnh báo, kênh gửi thông báo, chống bão cảnh báo (alert storm suppression), và quy trình khôi phục theo BR-16.

---

## 1. Bảng quy tắc cảnh báo cốt lõi (Core Alert Rules)

| Tên Cảnh Báo (Alert Name) | Chỉ số (SLI Metric) | Ngưỡng (Threshold) | Cửa sổ đánh giá (Window) | Mức độ (Severity) | Kênh thông báo (Receiver) | Hành động khắc phục |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ApiHigh5xxRate** | `rate(fitmate_http_requests_total{status_code=~"5.."}[5m]) / rate(fitmate_http_requests_total[5m])` | > 1.0% | 5 phút | **P1 (High)** | Slack `#fitmate-alerts`, PagerDuty | Xem log có TraceId, kiểm tra DB/network, rollback nếu vừa deploy. |
| **ApiHighLatency** | `fitmate_http_request_duration_ms{p95}` | > 500ms (read)<br>> 1000ms (write) | 5 phút | **P2 (Medium)** | Slack `#fitmate-alerts` | Kiểm tra CPU/RAM container, slow queries, DB pool. |
| **OutboxQueueLag** | `fitmate_outbox_queue_depth` | > 100 tin nhắn hoặc `oldest_age > 300s` | 3 phút | **P2 (Medium)** | Slack `#fitmate-alerts` | Xem runbook `outbox-lag.md`, kiểm tra third-party push/email. |
| **OutboxDeadLetterDetected** | `increase(fitmate_outbox_dead_letter_total[5m])` | > 0 tin nhắn | 1 phút | **P1 (High)** | Slack `#fitmate-alerts`, On-call | Xem `GET /api/v1/admin/outbox/dead-letter`, kiểm tra lỗi và replay. |
| **RefundsPendingStale** | `fitmate_refunds_oldest_age_seconds` | > 86.400s (24 giờ) | 15 phút | **P2 (Medium)** | Email Finance, Slack `#fitmate-finance` | Nhắc nhở Admin duyệt hoàn tiền theo chính sách nền tảng. |
| **DbPoolExhaustion** | `fitmate_db_connection_pool_active` | > 80% max pool | 2 phút | **P0 (Critical)** | PagerDuty, SMS Operator | Xem runbook `db-failover.md`, kill idle connections, scale connection pool. |
| **ExternalUptimeFailure** | HTTP GET `/health/ready` từ probe ngoài | != 200 OK | 2 lần thử (30s interval) | **P0 (Critical)** | PagerDuty, Call On-call | Server down hoặc DB đứt kết nối, kiểm tra VPS và container ngay. |

---

## 2. Kiểm soát bão cảnh báo & Chống gửi lặp (Suppression & Deduplication)

1. **Inhibition Rules (Quy tắc ức chế cảnh báo thứ cấp)**:
   - Khi `ExternalUptimeFailure` hoặc `DbPoolExhaustion` đang kích hoạt (Firing):
     -> Tự động **ức chế (suppress)** cảnh báo `OutboxQueueLag` và `ApiHighLatency` để tránh bão thông báo (alert storm) che lấp nguyên nhân gốc rễ (root cause).
2. **Grouping & Throttling**:
   - Gom nhóm theo `alertname` và `service`.
   - `group_wait: 30s`: Chờ 30 giây gom các cảnh báo liên quan trước khi gửi tin đầu tiên.
   - `group_interval: 5m`: Khoảng cách giữa các đợt gửi gom nhóm.
   - `repeat_interval: 4h`: Chỉ gửi lại cảnh báo cùng loại sau 4 giờ nếu sự cố chưa được giải quyết.
3. **Thông báo phục hồi (Resolved Notifications)**:
   - Mọi cảnh báo khi hết vi phạm ngưỡng trong 2 chu kỳ đánh giá liên tiếp sẽ tự động gửi thông báo **[RESOLVED]** kèm tổng thời gian gián đoạn (outage duration) để đội ngũ vận hành nắm rõ.

---

## 3. Hệ thống giám sát Uptime ngoài máy chủ (External Uptime Monitor)

- **Nguyên tắc an toàn**: Không tự host dịch vụ uptime trên cùng node với API backend (vì khi node sập, monitor cũng sập theo).
- **Yêu cầu triển khai**:
  - Sử dụng dịch vụ probe độc lập (Uptime Kuma chạy trên VPS phụ, hoặc BetterUptime / UptimeRobot).
  - Probe 1: `GET https://<api-domain>/health/live` (tần suất 15 giây, timeout 3 giây).
  - Probe 2: `GET https://<api-domain>/health/ready` (tần suất 30 giây, timeout 5 giây).
- **Trạng thái cấu hình**:
  - Kênh webhook Slack/PagerDuty và dịch vụ external probe: **BLOCKED (Chờ Operator cung cấp webhook URL theo OPS-04/OPS-05)**.
  - Phía Backend đã hoàn tất chuẩn hóa endpoint `/health/live`, `/health/ready`, `/api/v1/admin/metrics/prometheus` và metrics sẵn sàng kết nối.

---

## 4. Kịch bản diễn tập sự cố (Rehearsal & Failure Injection)

1. **Giả lập lỗi DB (DB Connection Drop)**:
   - Dừng tạm thời container Postgres: `docker compose stop postgres`.
   - Quan sát `/health/ready` chuyển sang `503 Unhealthy` với chi tiết `Database unreachable`.
   - Bật lại: `docker compose start postgres`.
   - Quan sát `/health/ready` tự động phục hồi `200 Healthy`.
2. **Giả lập Outbox Dead-Letter**:
   - Enqueue tin nhắn outbox với handler ném lỗi giả định.
   - Sau 5 lần retry (với backoff), tin nhắn chuyển sang `Status = "dead_letter"`.
   - Admin gọi `POST /api/v1/admin/outbox/dead-letter/{id}/replay` kèm lý do.
   - Kiểm tra tin nhắn chuyển về `pending` và bảng `audit_logs` có bản ghi kiểm toán chính xác.

