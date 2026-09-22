# So sánh Managed PostgreSQL — cho production Fitmate

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> Viết 2026-09-18. Cập nhật 2026-09-18 (phiên 7) theo DEC-030.
>
> **Phạm vi**:
> - Baseline kỹ thuật để lấy báo giá + kiểm thử (đã chốt ở DEC-030).
> - Cập nhật các nguyên tắc khi so sánh: bỏ số liệu chưa được xác minh chính thức (giá list, latency, connection limit); không kết luận provider không phù hợp chỉ vì secret dự án cũ bị lộ; phân biệt standby HA vs read replica.
> - So sánh vẫn giữ để team có thể revisit sau khi có budget/credentials; không thay thế việc xác minh SKU/giá từ nguồn chính thức.
>
> **KHÔNG tự tạo tài nguyên trả phí** trên bất kỳ provider nào (rule AGENTS.md + UA-023 chưa chốt credentials).

## 1. Bối cảnh Fitmate

- **Workload đặc trưng**: 80% read, 20% write; 95% transaction nhỏ (< 50 KB); vài upload media (1-5 MB) qua app.
- **Connection peak ước tính**: ~150 connection đồng thời (xem `capacity-plan.md` § 2). Nếu app scale lên 3-5 replicas thì cần pool + connection multiplexer.
- **Yêu cầu tuyệt đối**:
  - Backup ít nhất 7 ngày, có thể PITR 1 giờ.
  - Region Asia/Southeast (Singapore hoặc TP.HCM) — giảm latency cho user Việt Nam.
  - Có TLS connection bắt buộc (chứa PII + payment metadata).
  - Phải có read replica option (khi admin dashboard query nặng).

## 2. Ứng viên

> Số liệu trong bảng dưới ghi chú là **ước tính từ trang chính thức tại ngày viết**. **Phải xác minh lại SKU, pricing, region availability, connection limits, backup/PITR/PITR window tại nguồn chính thức của provider** trước khi cấp phát tài nguyên trả phí — giá list công khai không phải budget cuối.

### A. Supabase (managed Postgres)

- **Region**: có region Singapore (Asia).
- **Connection limit / pricing / backup**: **chưa xác minh** tại ngày viết. Cần sales / pricing page chính thức.
- **Ưu (chung)**: Auth, Storage, Realtime tích hợp sẵn.
- **Nhược (chung)**: vendor lock-in trung bình; tùy thuộc dashboard của họ.
- **Quan trọng**: secret Supabase cũ đã lộ trong Git history của project cũ. Theo `decisions.md` DEC-024 + DEC-031: **secret lộ phải rotate/revoke bất kể provider mới** — việc này là UA-001, không phải lý do loại provider. Supabase hoàn toàn có thể là ứng viên nếu tạo project mới, không migrate data từ project cũ.

### B. Neon (serverless Postgres)

- **Region**: có region Singapore (aws-ap-southeast-1).
- **Connection limit / pricing / backup retention / PITR window**: **chưa xác minh**.
- **Ưu**: serverless — auto-scale compute theo query; tier free cho dev.
- **Nhược**: có cơ chế auto-suspend → có thể có cold start khi wake. **Chưa xác minh** độ trễ cold-start.
- **Không kết luận "không phù hợp"** chỉ vì lo ngại cold start — cần benchmark thực tế.

### C. AWS RDS for PostgreSQL

- **Region**: ap-southeast-1 (Singapore), ap-southeast-2 (Sydney).
- **Connection / pricing / backup window**: tùy instance class — **chưa xác minh** tại ngày viết.
- **Ưu**: enterprise-grade, IAM auth, encryption at rest, VPC private, integration với AWS ecosystem.
- **Nhược**: giá cao hơn các lựa chọn khác; cần VPC/subnet/SG setup.
- **Phù hợp** nếu Fitmate dùng AWS cho domain/email/CDN — chưa quyết định.

### D. DigitalOcean Managed Postgres

- **Region**: có `sgp1` (Singapore).
- **Connection limit / pricing / backup retention / PITR / HA**: **chưa xác minh** tại ngày viết. Tier Basic có connection limit thấp hơn các plan cao hơn.
- **Ưu**: dashboard đơn giản; phù hợp workload vừa.
- **Nhược**: ít region ở Asia (Singapore, Bangalore, Sydney); không có VPC peering sâu.
- **Đã chốt ở DEC-030** là baseline production target.

### E. Self-host Postgres trên VPS

- **Region**: tùy VPS provider.
- **Connection / backup**: tự setup.
- **Ưu**: kiểm soát 100%, giá thấp nhất.
- **Nhược**:
  - SPOF (single point of failure) — 1 VPS = 1 instance, downtime khi node fail hoặc disk đầy.
  - Phải tự vận hành: monitoring, backup verify, security patch, WAL archive, replication.
  - Không có HA tự động — cần tự setup `patroni` hoặc replica failover.
  - Chưa có runbook restore đã test.
- **Phù hợp**: dev / staging. Production cần ít nhất 1 replica + monitoring đầy đủ + restore test mỗi quý.
- **Không kết luận "không phù hợp production"** — chỉ là lựa chọn cần thêm công sức vận hành.

## 3. Bảng so sánh nhanh (ước tính — phải xác minh)

| Tiêu chí | Supabase | Neon | AWS RDS | DO Managed | Self-host VPS |
|---|---|---|---|---|---|
| Region Asia gần VN | Có (SG) | Có (SG) | Có (SG) | Có (SG) | Tùy |
| Connection limit (chưa xác minh theo tier) | ❓ | ❓ | ❓ | ❓ | ❓ |
| Backup retention (chưa xác minh theo tier) | ❓ | ❓ | ❓ | ❓ | Tự setup |
| PITR window | Có (paid) | Có (paid) | Có | Có (higher tier) | Tự setup |
| HA tự động | Có | Có | Có (Multi-AZ) | Có | ❌ — phải tự setup |
| **Standby HA** (failover, không query) | Có | Có | Có | Có | Tự setup |
| **Read replica** (scale read) | Có | Có | Có | Có (higher tier) | Tự setup |
| Effort vận hành | Thấp | Thấp | Trung bình | Thấp | Cao |
| Cold start risk | Không | Có thể (chưa benchmark) | Không | Không | Không |
| Giá list public (chưa dùng làm budget) | (chưa xác minh) | (chưa xác minh) | (chưa xác minh) | (chưa xác minh) | (rẻ nhất) |

> **Quan trọng**:
> - Hai khái niệm **Standby HA** và **Read replica** là KHÁC NHAU. Standby chỉ chạy khi primary fail; read replica dùng để scale read traffic.
> - Việc ghi "phù hợp / không phù hợp" dựa trên evidence chưa xác minh là kết luận sớm — chỉ ghi nhận provider CÓ option đó; pricing riêng từng tier phải verify trên trang chính thức hoặc sales.
> - **Không dùng secret lộ làm lý do loại provider**. Secret lộ → rotate bất kể provider. (Quyết định loại provider phải dựa cost/HA/region/operations.)

## 4. Khuyến nghị cho từng môi trường

### Development / local
- **Self-host Postgres qua Docker Compose** (đã có trong `compose.yml`).
- Mount volume local, xóa là reset. Không cần backup.

### Staging
- **Môi trường tách biệt**, không dùng credentials/dữ liệu production.
- Phương án A: self-host 1 droplet Postgres + `pg_dump` nightly → S3 bucket staging. Retain 7 ngày. Không cần PITR.
- Phương án B: managed cluster staging riêng (DigitalOcean staging tier thấp hơn prod) — chốt khi có budget cụ thể.
- Lý do: staging cần reset/rebuild thường xuyên, không cần HA, chi phí thấp.

### Production (baseline đã chốt ở DEC-030)
- **DigitalOcean Managed Postgres** (`sgp1` Singapore).
- Primary: **2 vCPU / 4 GiB RAM**, storage ~30 GiB.
- **1 standby HA** (failover, không query).
- **Chưa có read replica** — phân biệt rõ với standby.
- Cấu hình này là **baseline kỹ thuật**, không phải cam kết số user chịu được. Điều chỉnh theo load test + budget được duyệt.
- **Không tự tạo cluster** — chờ UA-023 (credentials qua secret manager).

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
