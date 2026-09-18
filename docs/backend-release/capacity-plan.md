# Capacity Plan

> Mô hình tải cho hệ thống. **Giả định** vì chưa có số liệu kinh doanh thật. Sẽ đo lại khi có production traffic và cập nhật file này.

## 1. Loại người dùng & hành vi (giả định MVP)

| Loại | Số ước tính | Hành vi |
|---|---|---|
| Member active | 5.000 DAU | 3 lần / ngày mở app; 1 booking / tuần; 5 API call / phiên. |
| PT active | 200 DAU | 10 lần / ngày mở app; 20 booking / tuần; 50 API call / phiên. |
| Admin | 5 | 4 giờ / ngày dashboard; refresh mỗi 30s. |
| Tổng request | ~50 RPS peak, ~10 RPS average. |

## 2. Peak concurrency

- Booking peak (giờ vàng 18–22h): 100 concurrent request tạo hold.
- Chat AI peak: 20 concurrent (rate-limited).
- Upload image peak: 5 concurrent.
- Dashboard admin refresh: 5 concurrent.

## 3. Read/write mix

- 80% read (GET), 20% write (POST/PATCH/DELETE).
- Hot read: dashboard admin, marketplace PT, social feed.

## 4. Kích thước dữ liệu

- Users: ~10.000 trong năm đầu.
- Bookings: ~100.000 / năm (~2.000 / tuần).
- Posts: ~50.000 / năm.
- Chat messages: ~500.000 / năm.
- Workout sessions: ~500.000 / năm.
- Payment records: ~100.000 / năm.
- Audit log: ~20.000 / năm.

## 5. Upload / media

- Avg image upload: 1MB (jpeg). Peak 5 concurrent → 5MB/s.
- Certificate: 5MB. Ít.

## 6. Chat realtime (MVP chưa có)

- Khi có: ước tính 5.000 connections đồng thời. Cần sticky session + WS infra. **Chưa xây** trong MVP.

## 7. AI concurrency (chờ nhóm AI)

- Meal scan: 10 concurrent.
- Chat AI: 20 concurrent.

## 8. Mục tiêu latency / error rate

| Endpoint | p50 | p95 | p99 | error rate |
|---|---|---|---|---|
| Auth (login/register) | 200 ms | 500 ms | 1 s | < 0.5% |
| Read đơn giản | 50 ms | 150 ms | 300 ms | < 0.1% |
| List admin (offset) | 100 ms | 300 ms | 800 ms | < 0.1% |
| Booking hold | 80 ms | 250 ms | 500 ms | < 0.5% |
| Payment create | 150 ms | 500 ms | 1 s | < 0.5% |
| Upload | 300 ms | 1 s | 3 s | < 1% |
| AI meal scan | 2 s | 5 s | 8 s | < 1% (khi AI lên) |
| AI chat | 1 s | 4 s | 8 s | < 1% (khi AI lên) |

## 9. Cấu hình hạ tầng (đề xuất)

- **App**: 1 instance Docker container, 2 vCPU, 4 GB RAM. Stateless.
- **Postgres**: 2 vCPU, 4 GB RAM, 50 GB SSD. Backup nightly.
- **MailHog** (dev/staging) hoặc SMTP provider (prod) — không tốn tài nguyên.
- **Tổng**: ~6 vCPU, 12 GB RAM cho staging.

## 10. Giới hạn cấu hình hiện tại (cần lưu ý)

- 1 instance BE → bottleneck ở CPU cho AI call hoặc heavy aggregation.
- Postgres connection pool mặc định 100 (Npgsql) → tăng khi cần.
- Không có Redis → không cache; mọi read trực tiếp DB.
- Không có CDN → static asset từ BE.

## 11. Mở rộng

- Scale ngang app khi CPU > 70% peak hoặc p95 > mục tiêu.
- Vertical scale Postgres trước khi read replica (cost-effective hơn ở quy mô này).
- Thêm Redis khi:
  - Cache dashboard > 5s query, refresh > 10 RPS.
  - Session store cần shared (khi chạy > 1 instance).
- Tách AI ra service riêng khi đo thấy latency > ngưỡng ảnh hưởng read path.

## 12. Khi nào mô hình này cần đo lại

- Sau 30 ngày production: dùng số liệu thật.
- Sau mỗi feature flag launch lớn.
- Trước khi tăng capacity (scale plan).
