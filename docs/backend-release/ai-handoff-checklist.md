# AI handoff — dự kiến nhận file .pt
Cập nhật 2026-09-23. Hiện chưa có artifact; các ô bên dưới CHƯA xác nhận.
Không chặn backend preparation vì chưa đủ thông tin; hỏi nhóm AI các thiếu sót khi bàn giao thực tế.

## A. Gói tối thiểu cần kiểm tra khi nhận
- [ ] File .pt, version/checksum/nguồn và người bàn giao.
- [ ] Mỗi file phục vụ capability nào; một hay nhiều model.
- [ ] Cách lưu file: weights/state_dict, serialized model, TorchScript hay định dạng khác — do team AI xác nhận, không suy từ extension.
- [ ] Code load + inference chạy được; model architecture nếu artifact yêu cầu.
- [ ] Python/PyTorch/dependencies phiên bản và OS/architecture đã thử.
- [ ] Labels/tokenizer/config/preprocessing/postprocessing/dataset phụ trợ nếu cần.
- [ ] Một input mẫu hợp lệ + output mong đợi cho mỗi capability; không dùng PII thật.
Nếu chỉ có .pt mà chưa có cách chạy, ghi RECEIVED/INCOMPLETE và danh sách thiếu; không tự dựng kiến trúc model hoặc fake inference.

## B. Trước chọn deployment
- [ ] CPU/GPU requirements, RAM/VRAM/disk, cold/warm latency và concurrency đã đo.
- [ ] Sync/async/streaming; batch support/cancellation; max input/output.
- [ ] Inference có gọi mạng/download model/telemetry không; data/privacy và egress.
- [ ] License/quyền dùng thương mại, nguồn assets/model phụ trợ và limitations.
- [ ] Confidence semantics nếu có; không yêu cầu fabricated chat confidence.
- [ ] Cách health/readiness/warmup, shutdown, upgrade/rollback.
Không mua GPU/cloud hoặc hardcode Python service trước khi biết requirements.

## C. Quy trình tích hợp
1. Ghi nhận file/checksum và inventory, lưu artifact ngoài tracked source; chưa execute/load trực tiếp vào API.
2. Review entrypoint/dependencies và giới hạn quyền; artifact model có thể cần deserialization nên chỉ chạy nguồn tin cậy trong môi trường cô lập.
3. Theo hướng dẫn nhóm AI chạy sample trên môi trường được phép; nếu user đang yêu cầu chạy tay, cung cấp commands để user chạy.
4. Đối chiếu preprocessing/units/labels/output; ghi gap so với ai-contract.md. Không tự suy macros từ food label.
5. Đo latency/memory/resources, chọn runtime/transport và sync-vs-job; mới viết adapter cụ thể và deployment config.
6. Kiểm error/timeout/cancellation/concurrency/privacy và contract fixtures bằng output thực.
7. Tích hợp staging Android flow; operator duyệt bật từng capability, giữ kill switch/manual fallback.
8. Version-pin artifact; checksum mismatch hoặc thiếu required files không enable.

## D. Metadata template (để trống đến khi biết)
- Artifact name/version/SHA256:
- Capability mapping:
- Format/save method:
- Load/inference entrypoint:
- Dependency lock/runtime:
- Required companion files:
- Input schema/preprocessing:
- Output schema/postprocessing:
- Confidence meaning (hoặc not provided):
- Hardware/resource measurements:
- Network/license/privacy constraints:
- Known unsupported cases:
- Owner/support contact:
- Validation commands/results:
- Selected deployment/rollback (sau đánh giá):
Không commit model weights, secrets, health images hoặc chat samples của user; tham chiếu artifact storage/version phù hợp khi đã chốt.
