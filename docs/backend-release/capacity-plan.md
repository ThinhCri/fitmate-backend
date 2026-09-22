# Capacity plan — production baseline
PROD-2026-09-22-v1. Các số dưới đây là mục tiêu kiểm thử, không phải số người dùng thật hoặc năng lực đã đo.
## Mô hình
Không dùng “80k downloads” để suy concurrency. DAU, sessions/user/day, requests/session phải có nguồn analytics sau launch.
Average RPS = DAU * sessions_per_day * requests_per_session / 86400.
Ví dụ GIẢ ĐỊNH 1000 DAU * 2 sessions * 30 requests /86400 ≈0.69 RPS; peak factor và background/push/chat phải đo riêng.
Traffic test ban đầu: 40% read feed/profile,20% workout/nutrition read/write,15% chat,15% booking/payment sandbox,10% admin/auth. Không chạy load vào provider live.
Dataset giả lập: 10k users,500 PT,100k posts/messages,100k workout/meal logs; distribution và indexes phải ghi trong report.
Targets: normal50RPS/30min, peak100RPS/10min, soak20RPS/2h; thresholds xem release-checklist.
## Topology
API Compose/VPS + worker; managed PostgreSQL Singapore primary2vCPU/4GiB + standby HA theo DEC-030 là baseline cần quote/approve.
Standby không phải read replica; API một node là SPOF. Không claim HA toàn hệ thống chỉ nhờ DB HA.
Pool budget = tổng API/worker instances * max pool + migration/operator headroom <= actual DB connection budget.
Storage local chỉ được production nếu persistent/backup/capacity đáp ứng; scale nhiều node phải shared durable object storage hoặc topology phù hợp.
Không lưu giá/quota/SKU giả định làm quyết định mua. OPS-05 xác minh giá/capacity/backup/region tại thời điểm provision.
## Đo và nâng cấp
Ghi p50/p95/p99, errors, throughput, CPU/RAM, DB pool wait/locks/slow queries, queue age/dead letters.
CPU>70% sustained hoặc pool wait/queue lag vi phạm mục tiêu: profile/query/index trước khi scale; retest.
RPO<=24h/RTO<=4h là mục tiêu, không khẳng định provider đã hỗ trợ.
BE-823 lưu env/image/commit/seed/load scripts/raw report; smoke chỉ chứng minh smoke.
