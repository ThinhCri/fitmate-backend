# AI Handoff Checklist

> Khi nhóm AI bàn giao gói AI, dùng checklist này để nhận và verify. **KHÔNG tự ý train, chọn provider, hay giả định runtime** trước khi có bàn giao.

## A. Thông tin cần từ nhóm AI

### 1. Artifact

- [ ] Format: ONNX / TorchScript / Python wheel / Docker image / binary khác.
- [ ] Version (semver hoặc internal).
- [ ] Digest (SHA256) cho binary/image.
- [ ] Có reproducible build không.

### 2. Runtime

- [ ] OS & architecture (linux/amd64, linux/arm64, ...).
- [ ] Python version (nếu Python).
- [ ] Dependencies (requirements.txt / image base digest).
- [ ] Phiên bản ONNX Runtime / Torch / TensorRT.

### 3. Hardware

- [ ] CPU-only hay cần GPU?
- [ ] Loại GPU (A100 / L4 / T4 / ...).
- [ ] VRAM tối thiểu.
- [ ] RAM tối thiểu.
- [ ] Disk cho model + cache.
- [ ] Concurrent request tối đa (số worker / batch size).

### 4. Command & lifecycle

- [ ] Startup command.
- [ ] Inference command (HTTP / gRPC / CLI?).
- [ ] Health endpoint.
- [ ] Graceful shutdown.

### 5. I/O

- [ ] Input schema (mime, size limit, format).
- [ ] Output schema (JSON shape, units).
- [ ] Sample input/output files.
- [ ] Pre/postprocessing ownership: AI team làm, hay BE làm?

### 6. Performance

- [ ] Cold start latency.
- [ ] Warm inference latency p50/p95/p99.
- [ ] Throughput (req/s).
- [ ] Behavior khi vượt capacity (queue / 429 / drop?).

### 7. Network

- [ ] Có gọi outbound (model update, telemetry)?
- [ ] Có cần allowlist domain/IP?
- [ ] Có cần proxy?

### 8. Health & readiness

- [ ] `/health/live`, `/health/ready` shape.
- [ ] Có warm-up cần thiết?
- [ ] Memory growth theo thời gian?

### 9. Logging

- [ ] Format (JSON / text).
- [ ] Mức log default.
- [ ] Có log request payload? (KHÔNG cho phép nếu có PII).
- [ ] Retention.

### 10. License & pháp lý

- [ ] License.
- [ ] Có dùng model có ràng buộc thương mại không?
- [ ] Data dùng để train có nguồn gốc?

### 11. Limitations

- [ ] Known failure modes.
- [ ] Domain ngoài phạm vi.
- [ ] Bias đã phát hiện.
- [ ] Confidence semantics: `confidence` có calibrated không?

### 12. Upgrade & rollback

- [ ] Cách upgrade (image tag).
- [ ] Cách rollback (image tag cũ).
- [ ] Có cần reindex/retrain khi đổi model?

### 13. Hỗ trợ

- [ ] Người liên hệ.
- [ ] SLA nếu có.
- [ ] Kênh báo lỗi.

## B. Quy trình nhận & verify (phía BE)

1. Chạy artifact trong môi trường cô lập (Docker, không expose public).
2. Verify contract:
   - Gửi sample input theo `ai-contract.md`.
   - Verify output match schema + confidence ∈ [0,1].
   - Verify error case (no result, missing data, timeout, invalid output).
3. Đo tài nguyên thực tế khi chạy 5/20/50 concurrent.
4. Đo latency p50/p95/p99.
5. Quyết định deployment:
   - **In-process** (nếu artifact là ONNX và < 2GB, latency OK).
   - **Sidecar container** (khuyến nghị cho MVP; gọi qua HTTP localhost).
   - **Separate service** (chỉ khi đo thấy bottleneck).
6. Thêm vào Compose profile `ai`.
7. Cập nhật `architecture.md` và `decisions.md`.
8. Smoke end-to-end với FE.

## C. Khi chưa bàn giao

- Adapter trả `FITMATE.AI.UNAVAILABLE`.
- Không có fake result.
- Không có fake provider trong production env.
- Tất cả tính năng không phụ thuộc AI vẫn chạy bình thường.

## D. Khi bàn giao thiếu mục nào

Dừng nhận. Ghi rõ blocker vào `handoff.md`. Không tự giả định.
