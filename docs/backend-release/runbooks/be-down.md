# Runbook — BE down

> Khi BE không phản hồi / health check fail.

## Triệu chứng

- `/health/ready` trả 503 hoặc timeout.
- Users báo lỗi 5xx.
- Monitoring cảnh báo.

## Bước xử lý

1. **Xác định phạm vi**:
   - 1 instance hay nhiều instance?
   - Local lỗi hay VPS lỗi?
2. **Check logs**:
   - `docker logs <be> --tail 200`.
   - Tìm exception, OOM, panic.
3. **Check resources**:
   - `docker stats`.
   - Disk full?
   - OOM killed?
4. **Check DB**:
   - `docker logs <postgres> --tail 200`.
   - `psql -c "select 1"` từ BE container.
5. **Check upstream**:
   - Reverse proxy (Caddy/Nginx) lỗi?
   - DNS resolve?
   - TLS cert expire?
6. **Restart nhanh**:
   - `docker compose restart be`.
7. **Nếu vẫn fail**:
   - Rollback image về tag trước (xem `deployment-runbook.md`).
8. **Nếu DB corrupt**:
   - Xem `db-failover.md`.

## Sau sự cố

- Ghi root cause vào `handoff.md`.
- Tạo task phòng ngừa (vd: "add disk alert", "fix memory leak").

## Liên hệ

- Owner: backend team.
- Escalation: tech lead.
- User-facing status: cập nhật status.fitmate.vn nếu có.
