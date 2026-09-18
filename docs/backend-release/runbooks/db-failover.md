# Runbook — DB failover / restore

> Khi Postgres corrupt / fail / cần restore từ backup.

## Triệu chứng

- `/health/ready` fail.
- BE trả 5xx do DB error.
- Disk full trên volume Postgres.

## Bước xử lý

1. **Ngừng ghi**:
   - `docker compose stop be` (tránh thêm data mới).
2. **Verify backup**:
   - Liệt kê file backup.
   - Chọn bản gần nhất trước sự cố.
3. **Restore**:
   - `docker compose down postgres`.
   - Mount backup vào container mới.
   - `pg_restore -d fitmate <backup>`.
   - Hoặc dùng `pg_basebackup` + WAL replay.
4. **Verify data**:
   - `psql` connect.
   - Count row các bảng chính.
   - So với metric pre-incident.
5. **Start BE**:
   - `docker compose up -d be`.
   - Health check `/health/ready`.
6. **Smoke**:
   - Login demo.
   - 1 booking flow.
7. **Notify**:
   - Cảnh báo users.
   - Cập nhật status page.

## Verify restore time

- Ghi vào `handoff.md`: thời gian từ incident → restored.

## RPO / RTO

- RPO đề xuất: ≤ 24h (nightly backup). Cần user chốt.
- RTO đề xuất: ≤ 2h. Cần user chốt.

## Phòng ngừa

- Bật continuous archiving (WAL) → giảm RPO.
- Replication async → tự động failover.
- Test restore mỗi quý (kịch bản trong `capacity-plan.md`).
