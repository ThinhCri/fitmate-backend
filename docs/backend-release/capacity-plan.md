# Capacity Plan v2 — có SPOFs, thresholds, điều kiện nâng cấp

> Viết 2026-09-18. **Mô hình giả định** (chưa đo thật). Sẽ đo lại bằng k6 khi có staging.

## 1. Mục tiêu

- Xác định cấu hình khởi đầu phù hợp budget < $150/mo cho staging+production.
- Liệt kê Single Points of Failure (SPOF) và cách giảm.
- Đặt thresholds đo được (RPS, latency, error rate, DB conn).
- Điều kiện nâng cấp cụ thể — không "nâng khi thấy chậm".

## 2. Giả định đầu vào

| Tham số | Giá trị | Nguồn |
|---|---|---|
| Member DAU (3 tháng đầu) | 1.000 | Số liệu nội bộ Fitmate |
| Member DAU (12 tháng) | 5.000 | Growth target |
| PT DAU | 50 → 200 | Số liệu nội bộ |
| Booking / tuần | 200 → 1.000 | Member × 1 booking/tuần |
| Posts / tuần | 100 → 500 | 10% member active social |
| Workout sessions / ngày | 500 → 2.500 | 50% member log daily |
| Meal logs / ngày | 200 → 1.000 | 20% member |
| Chat messages (friend + AI) / ngày | 1.000 → 5.000 | 2/user/day avg |
| Admin active | 5 | Không scale |
| Requests / session (mobile) | 15 avg | Tính qua screens |
| RPS average | 5 → 30 | (DAU × 15 req / ngày) / 86.400s |
| RPS peak | 50 → 100 | 18-22h, gấp 3× average |

**Không bao gồm** AI inference latency (chờ handoff) — coi như worker tách.

## 3. Cấu hình khởi đầu (đề xuất)

### 3.1 App server

- **Provider**: DigitalOcean Droplet Singapore (`sgp1`).
- **Tier**: General Purpose 4 GB ($24/mo).
- **Resource**: 2 vCPU, 4 GB RAM, 80 GB SSD, 4 TB transfer.
- **Container**: 1 instance BE .NET 8. Stateless. Listen `:8080`.
- **Bộ nhớ**: app ~500 MB, Postgres conn pool ~50 MB, headroom 3 GB cho spike.
- **Connection limit Npgsql pool**: `Maximum Pool Size=80` (để lại headroom cho DB-side processes).

### 3.2 Database

- **Provider**: DigitalOcean Managed Postgres (Singapore).
- **Tier**: General Purpose 4 GB + 1 read replica (~$60-80/mo, cần quote).
- **Storage**: 50 GB SSD (auto-scale tới 1 TB).
- **Connection limit**: ~100 cùng lúc (DO Basic 1 GB chỉ có 22 → KHÔNG đủ).
- **Backup**: 7-day automated + PITR window 24h (higher tier).
- **PgBouncer**: KHÔNG cần ở MVP — app chỉ có 1 instance, kết nối trực tiếp. Thêm khi scale > 2 app instances.

### 3.3 Admin web hosting

- **Option A (recommended)**: Vite build static → CDN.
  - Cloudflare Pages (free) hoặc DigitalOcean App Platform static ($0-3/mo).
  - Caching tốt, edge gần user.
- **Option B**: Cùng VPS với BE (nginx reverse proxy), bundle static files.
  - Đơn giản nhưng không CDN.
- **Quyết định**: Chọn A (Cloudflare Pages) cho production; B cho staging để giảm cost.

### 3.4 Storage (media)

- MVP: dùng DO Spaces (S3-compatible, Singapore) — 250 GB storage + 1 TB transfer = $5/mo.
- Bucket: `fitmate-prod-media` (private) + signed URL TTL 15 phút cho access.
- Backup: versioning enabled + lifecycle rule xóa file > 90 ngày nếu không tham chiếu.

### 3.5 Email

- Provider: SendGrid / Mailgun / AWS SES. Fitmate đề xuất **Mailgun** (giá rẻ, có sandbox).
- Free tier: 100 email/ngày (development).
- $35/mo: 50k email (production nếu scale).

### 3.6 Monitoring + backup

- **Monitoring**: Uptime Kuma self-host ($0, chạy trên cùng VPS) + BetterStack free tier (5 monitor).
- **Logging**: Serilog → Loki (self-host) hoặc Logtail free tier (5 GB/mo).
- **Backup DB**: DO managed backup + manual `pg_dump` mỗi tuần → DO Spaces `fitmate-backups` bucket.
- **Backup config**: dotnet `appsettings.json` không commit, lưu DO env vars.

### 3.7 Tổng chi phí (ước tính tháng đầu)

| Hạng mục | Giá |
|---|---|
| BE droplet | $24 |
| DB managed (GP 4 GB + replica) | $70 |
| Spaces 250 GB | $5 |
| Mailgun | $0 (free tier) |
| Cloudflare Pages (admin) | $0 |
| Uptime Kuma | $0 |
| Logtail free | $0 |
| Domain (nếu cần mua mới) | $12/năm |
| **Tổng** | **~$100-105/mo** |

## 4. Single Points of Failure (SPOF) hiện tại

| SPOF | Nguy cơ | Cách giảm |
|---|---|---|
| **1 BE instance** | Nếu crash → downtime toàn bộ API. | Health check + auto-restart qua Docker restart policy `unless-stopped`. Lên kế hoạch scale 2 instances khi đo thấy CPU > 70% peak. |
| **1 DB primary** | Nếu fail → mất write capability. | DO Managed Postgres có standby tự động (Multi-node). Failover ~30-60s. |
| **1 DB replica** | Nếu fail → giảm read throughput cho dashboard admin. | Không critical cho MVP — dashboard admin không cần real-time. Có thể bỏ replica ở tháng đầu tiết kiệm $30. |
| **VPS đơn AZ** | Nếu DO region sgp1 sập → mất toàn bộ. | Snapshot backup hàng ngày restore được ở region khác trong ~4h. |
| **SMTP provider** | Nếu Mailgun sập → email không gửi được. | Fallback: dùng SMTP relay từ VPS (Postfix) — nhưng tăng effort vận hành. Chưa làm ở MVP. |
| **Payment provider (VNPay)** | Nếu VNPay sập → booking không thanh toán được. | App hiển thị "Payment đang bận, thử lại sau". Booking tạm giữ đến khi retry. |
| **Push notification provider** | Nếu FCM sập → notification không gửi. | In-app notification vẫn hoạt động. Acceptable. |
| **CDN cho admin web** | Nếu Cloudflare sập → admin không truy cập được. | Fallback: serve từ VPS. |
| **TLS certificate** | Nếu Let's Encrypt không renew → HTTPS fail. | Auto-renew + alert trước 14 ngày. Backup: mua cert từ commercial CA. |

## 5. Giới hạn đo được (đặt trước khi đo)

### 5.1 Latency

| Endpoint | p50 | p95 | p99 |
|---|---|---|---|
| `POST /auth/login` | 200 ms | 500 ms | 1 s |
| `GET /trainers` (marketplace) | 50 ms | 200 ms | 500 ms |
| `GET /me/bookings` | 80 ms | 300 ms | 800 ms |
| `POST /bookings/holds` (slot lock) | 100 ms | 250 ms | 600 ms |
| `POST /bookings/{id}/payments` | 200 ms | 600 ms | 1.5 s |
| `GET /admin/finance/overview` | 200 ms | 1 s | 2 s (cached thì < 500 ms) |
| `POST /me/workout/sessions` | 100 ms | 300 ms | 800 ms |

### 5.2 Throughput

| Test | RPS | Duration | Verdict |
|---|---|---|---|
| Smoke | 5 | 1 min | All < thresholds |
| Normal | 30 | 10 min | p95 trong ngưỡng |
| Peak | 100 | 5 min | p95 trong ngưỡng, no error |
| Spike | 0→200 | 10 min | Graceful degrade, < 2% error |
| Soak | 30 | 1 hour | Memory stable, no leak |

### 5.3 Error rate

- Auth endpoint: < 0.5% (do BCrypt verify, JWT issue)
- Read endpoint: < 0.1%
- Write endpoint: < 0.5%
- Payment endpoint: < 1% (external provider variance)
- Health check: 0%

### 5.4 Resource saturation

- CPU average: < 60%, peak < 85%
- RAM: < 80% sustained
- DB connection pool: < 80% capacity (Npgsql pool)
- DB CPU: < 70%
- Disk: < 80% (alert ở 70%)
- DB replication lag: < 5s

## 6. Điều kiện nâng cấp (không "nâng khi thấy chậm")

| Trigger | Hành động | Effort |
|---|---|---|
| CPU peak > 70% liên tục 1 tuần | Scale lên 2 BE instances (load balancer + sticky session không cần vì JWT stateless). Cần DB pool size tăng hoặc PgBouncer. | ~1 ngày config |
| DB CPU > 70% peak | Vertical scale DO GP 4 GB → 8 GB ($35→$70). Hoặc thêm 1 read replica. | < 1 ngày |
| DAU > 5.000 | Thêm PgBouncer, scale BE lên 3 instances, thêm cache Redis cho dashboard. | 1 tuần |
| Booking > 200/giờ peak | Partition `appointments` theo `slot_start_utc` tháng. Index optimize. | 2-3 ngày |
| Latency p95 > threshold > 2 ngày liên tục | Investigate slow queries (pg_stat_statements). Thêm index. Cân nhắc denormalize (booking → service_title). | 1-2 ngày |
| Storage > 100 GB | Migrate từ DO Spaces sang Backblaze B2 (cheaper). Setup lifecycle policy. | 2-3 ngày |
| Uptime requirement > 99.9% | Multi-region deployment + DNS failover. **KHÔNG MVP**. | 2-4 tuần |
| Concurrent booking > 100 slot/s | Migrate slot lock từ DB sang Redis atomic. **Chưa cần ở MVP**. | 1-2 tuần |

## 7. Rủi ro lớn nhất

| Risk | Impact | Mitigation |
|---|---|---|
| Đo sai RPS peak → capacity không đủ | Outage khi launch campaign | Run spike test trước launch. Set auto-scale trigger (DEC-007 cần update). |
| VNP down → không accept payment | Mất revenue | Đã có fallback UX (xem vnpay-googleplay-policy.md §3). |
| DB connection pool exhausted | App hang | Monitor pool usage, alert > 80%. PgBouncer khi > 2 instances. |
| Memory leak trong app | Crash sau vài ngày | Soak test 1h. Memory profiling với dotMemory. |
| Disk full → log spam | App fail write | Log rotation + alert 70%. Structured log level production = Information. |
| Secret lộ (UA-001 vẫn chưa rotate) | Account compromise | BLOCKER. UA-001 phải xong trước khi production. |

## 8. Điều kiện KHÔNG nâng cấp (avoid over-engineering)

- **Không scale Redis riêng** chỉ vì "cache là tốt". Chỉ khi dashboard admin p95 > 1s.
- **Không thêm microservice** cho notification/email. Background worker trong process là đủ.
- **Không dùng Kubernetes** trước khi có > 10 instances hoặc > 50k DAU.
- **Không mua enterprise support** cho managed DB khi chưa có incident đáng kể.

## 9. Việc cần làm

1. Chốt cấu hình §3 với user.
2. Update `deployment-runbook.md` với IP, DNS, droplet ID sau khi provision.
3. Viết k6 scripts `tests/load/{smoke,normal,peak,spike,soak}.js`.
4. Chạy smoke + normal trên staging. Ghi kết quả vào `load-test-report.md`.
5. Đo peak trước khi launch production. Cập nhật capacity-plan nếu cần.

## 10. Quan trọng

- **Không tuyên bố "hỗ trợ N người dùng"** trước khi đo thật.
- **Không gọi "production-ready"** trước khi pass hết gate `release-checklist.md`.
- **Mỗi con số trong file này là giả định**; sau khi đo thật sẽ update lại.
