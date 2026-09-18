# So sánh Managed PostgreSQL — cho production Fitmate

> Viết 2026-09-18. So sánh theo tiêu chí: **chi phí, region, connection limits, backup/PITR, restore, khả năng mở rộng**. Đây là **đề xuất**, không tự tạo tài khoản trả phí. User/operator duyệt trước khi áp dụng.

## 1. Bối cảnh Fitmate

- **Workload đặc trưng**: 80% read, 20% write; 95% transaction nhỏ (< 50 KB); vài upload media (1-5 MB) qua app.
- **Connection peak ước tính**: ~150 connection đồng thời (xem `capacity-plan.md` § 2). Nếu app scale lên 3-5 replicas thì cần pool + connection multiplexer.
- **Yêu cầu tuyệt đối**:
  - Backup ít nhất 7 ngày, có thể PITR 1 giờ.
  - Region Asia/Southeast (Singapore hoặc TP.HCM) — giảm latency cho user Việt Nam.
  - Có TLS connection bắt buộc (chứa PII + payment metadata).
  - Phải có read replica option (khi admin dashboard query nặng).

## 2. Ứng viên

> Số liệu **tra cứu 2026-09-18** từ trang chính thức. Sẽ cần verify lại khi chốt và **lấy báo giá chính thức từ sales** (không dùng giá list công khai làm budget).

### A. Supabase (self-host hoặc managed)

- **Region**: Singapore, Mumbai, Frankfurt, US East/West. Có `asia-southeast1` (Singapore).
- **Connection limit**: 
  - Free: 60 connections.
  - Pro (~$25/mo): 200 connections.
  - Team/custom: cao hơn.
- **Backup**:
  - Free: không có PITR.
  - Pro: 7-day PITR.
  - Team: 14-day PITR.
- **Storage**: tối đa theo plan; auto-scale.
- **Ưu**: có Auth, Storage, Realtime tích hợp nếu muốn dùng (Fitmate KHÔNG dùng — đã chốt JWT custom). Dashboard quản trị dễ.
- **Nhược**: đã có vết lộ secret trong lịch sử Git. Migration từ Supabase project hiện sang project mới cần export/import.
- **Phù hợp**: nếu team đã quen Supabase workflow và muốn tiết kiệm effort quản trị.

### B. Neon (serverless Postgres)

- **Region**: US East/West, Frankfurt, Singapore (đầu 2026), Sydney. Có `aws-ap-southeast-1` (Singapore).
- **Connection limit**: 
  - Free: 100 connections.
  - Launch: 500 connections.
  - Scale: custom.
- **Backup**:
  - Free: 7-day retention.
  - Launch: 30-day retention.
  - Scale: custom.
- **PITR**: 7 ngày (Launch+), rewind window.
- **Ưu**: serverless — tự scale compute theo query. Auto-suspend khi không dùng. Free tier cho dev.
- **Nhược**: 
  - Cold start có thể 500-2000 ms khi wake từ suspend — ảnh hưởng UX nếu traffic thấp.
  - PITR bị giới hạn window.
  - Latency từ Việt Nam tới Singapore OK (~30-60 ms).
- **Phù hợp**: nếu traffic không đều và muốn tiết kiệm chi phí off-peak.

### C. AWS RDS for PostgreSQL

- **Region**: đầy đủ ap-southeast-1 (Singapore), ap-southeast-2 (Sydney). Cần check ap-southeast-3 (Jakarta) cho Việt Nam.
- **Connection limit**: tùy instance class. `db.t4g.medium` (2 vCPU, 4 GB) = ~300 connections pool.
- **Backup**:
  - Automated backup 7-35 ngày free.
  - PITR trong window backup.
  - Snapshot manual theo yêu cầu.
- **Storage**: gp3/gp2, auto-scale.
- **Ưu**: enterprise-grade, IAM auth, encryption at rest, VPC private, đã có kinh nghiệm nếu dùng AWS cho phần khác. Read replica dễ.
- **Nhược**: giá cao hơn (~$50-100/mo cho production nhỏ). Cần VPC, subnet, security group setup.
- **Phù hợp**: nếu Fitmate đã chọn AWS cho domain/email/storage/CDN.

### D. DigitalOcean Managed Postgres

- **Region**: Singapore, Bangalore, Sydney, Amsterdam, NY, SF. Có `sgp1` (Singapore).
- **Connection limit**: tùy plan. Basic 1 GB = 22 connections; General Purpose 4 GB = 100 connections; General Purpose 8 GB = 197 connections.
- **Backup**: daily backup 7 ngày free; PITR 7 ngày ở higher tier.
- **Storage**: 10 GB → 1 TB+, auto-scale.
- **Ưu**: giá hợp lý ($15-60/mo cho production nhỏ), dashboard đơn giản. Không cần VPC riêng.
- **Nhược**: ít region ở Asia (chỉ Singapore + Bangalore + Sydney), không có VPC peering sâu.
- **Phù hợp**: nếu budget gọn và workload vừa phải.

### E. Self-host Postgres trên VPS (phương án hiện tại)

- **Region**: tùy VPS provider (Singapore, Tokyo, Mumbai đều có).
- **Connection limit**: tùy VPS — `db.t3.small` (2 vCPU, 4 GB) chịu được ~150 connection pool.
- **Backup**: tự setup `pg_dump` nightly + upload S3-compatible. PITR cần `wal-g` + WAL archive (phức tạp hơn).
- **Ưu**: kiểm soát 100%, giá rẻ nhất ($5-20/mo VPS), không phụ thuộc vendor.
- **Nhược**: 
  - **Single point of failure**: 1 VPS = 1 instance = downtime khi node fail hoặc disk đầy.
  - **Phải tự vận hành**: monitoring, backup verify, security patch, WAL archive, replication.
  - **Không có HA tự động**: cần tự setup `patroni` hoặc replica failover.
  - **Quy trình restore chưa được test** (xem `runbooks/db-failover.md`).
- **Phù hợp**: dev/staging. Production: cần ít nhất 1 replica + monitoring đầy đủ + restore test mỗi quý.

## 3. Bảng so sánh nhanh

| Tiêu chí | Supabase Pro | Neon Launch | AWS RDS gp | DO Managed | Self-host VPS |
|---|---|---|---|---|---|
| Region Asia gần VN | ✅ SG | ✅ SG | ✅ SG | ✅ SG | ✅ Tùy |
| Giá (1 GB RAM equiv) | ~$25/mo | ~$20/mo | ~$60/mo | ~$15/mo | ~$10/mo |
| Connection limit (small plan) | 200 | 500 | ~150 | 100 | 150 |
| Backup mặc định | 7d PITR | 7d PITR | 7d automated + PITR | 7d daily | Tự setup |
| PITR | ✅ | ✅ | ✅ | ⚠️ higher tier | ⚠️ Tự setup |
| Read replica dễ | ✅ | ✅ | ✅ | ⚠️ cao cấp | ⚠️ Tự setup |
| HA tự động | ✅ | ✅ | ✅ (Multi-AZ) | ✅ | ❌ |
| Effort vận hành | Thấp | Thấp | Trung bình | Thấp | Cao |
| Cold start risk | Không | ⚠️ Có | Không | Không | Không |
| Đã có vết lộ secret trong Git | ⚠️ Có | Không | Không | Không | Không |
| Vendor lock-in | Trung bình | Trung bình | Cao (nếu dùng AWS) | Thấp | Không |

## 4. Khuyến nghị cho từng môi trường

### Development / local
- **Self-host Postgres qua Docker Compose** (đã có trong `compose.yml`).
- Mount volume local, xóa là reset. Không cần backup.

### Staging
- **Self-host Postgres trên 1 VPS staging** riêng (`staging-db.fitmate.vn`).
- Backup nightly `pg_dump` → S3 bucket (`fitmate-staging-backup`).
- Retain 7 ngày. Không cần PITR.
- Lý do: staging cần reset/rebuild thường xuyên, không cần HA, chi phí thấp.

### Production
- **Khuyến nghị chính**: **DigitalOcean Managed Postgres** (Singapore) — General Purpose 4 GB + 1 read replica.
  - Giá ước tính ~$60-80/mo (cần lấy quote chính thức).
  - Connection limit ~100, dùng PgBouncer để multiplex.
  - HA cơ bản (standby).
  - Backup 7 ngày + PITR.
  - Latency từ Việt Nam ~30-50 ms tới Singapore.
- **Phương án dự phòng**: **AWS RDS** nếu Fitmate đã dùng AWS (S3, SES, CloudFront). Đắt hơn 2x nhưng tích hợp tốt.
- **Không khuyến nghị**: Supabase Pro (đã có vết lộ secret, cần migrate sạch), Neon (cold start risk), self-host (SPOF + effort vận hành cao cho production).

## 5. Điều kiện cần chốt với user

| Câu hỏi | Phương án | Tác động |
|---|---|---|
| Fitmate có dùng AWS cho phần khác (domain/email/CDN) không? | Có → RDS. Không → DO Managed. | Stack thống nhất. |
| Budget monthly cho DB production? | < $50 → DO Basic 1 GB (không khuyến nghị vì connection limit thấp). $50-100 → DO GP 4 GB. > $100 → RDS. | Trade-off chi phí / HA. |
| User chấp nhận cold start 500-2000 ms của Neon không? | Có → Neon. Không → RDS/DO. | UX khi traffic thấp. |
| Cần read replica ngay MVP không? | Có → DO GP 4 GB+ hoặc RDS. Không → có thể giảm xuống Basic. | Chi phí tăng ~30-50%. |

## 6. Migration strategy khi chuyển sang managed

> Nếu đang chạy self-host, sau đó muốn chuyển sang managed:

1. Tạo managed cluster ở region giống VPS hiện tại.
2. Dùng `pg_dump` từ VPS → restore sang managed cluster.
3. Verify row count các bảng chính.
4. Đổi connection string trong `.env`.
5. Chạy smoke test (register/login/booking/finance).
6. Giữ VPS 7 ngày rollback.
7. Decommission VPS.

Thời gian downtime: ~10-30 phút (tùy DB size).

## 7. Việc cần làm

- User trả lời 4 câu hỏi § 5.
- Sau đó cập nhật `decisions.md` thành DEC-010 (managed Postgres choice) + DEC-011 (region).
- Cập nhật `deployment-runbook.md` với connection string pattern.
- Cập nhật `.env.example` với `DB_SSLMODE=require` (managed bắt buộc TLS).
- Cập nhật `release-checklist.md` gate "DB production-ready" với restore test gần nhất.

## 8. Quan trọng

- **Không tự tạo tài khoản trả phí** trên bất kỳ provider nào.
- **Không tự chạy `pg_dump` từ production hiện tại** (Supabase project có thể có data cá nhân chưa được consent xử lý).
- Việc rotate secret Supabase cũ (UA-001) là **tiền đề** trước khi chọn provider mới.
