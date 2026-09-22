# AI integration contract — chuẩn bị nhận .pt
Cập nhật 2026-09-23 theo user. Nhóm AI phụ trách toàn bộ nghiệp vụ AI đã thống nhất; dự kiến bàn giao file .pt.
Chưa biết file chứa gì, số model, runtime, tài nguyên hoặc I/O. Không suy ra từ đuôi file rằng có thể load trực tiếp bằng .NET hoặc chạy tất cả capability.

## 1. Trách nhiệm
- Nhóm AI: model, preprocessing/postprocessing, labels/tokenizer/dữ liệu phụ trợ và code inference cần để chạy đúng.
- BE: auth/ownership, input validation, adapter boundary, resource limits, errors, lưu kết quả theo nghiệp vụ, API cho FE.
- FE: input/loading/result/error, xác nhận/chỉnh meal hoặc workout suggestion.
Không tự train, viết model thay team AI, tự chọn cloud inference hoặc mua GPU.

## 2. Capability inventory (yêu cầu sản phẩm, chưa phải khả năng đã được model chứng minh)
| Capability | Hành vi BE chuẩn bị | Cần xác nhận khi nhận artifact |
|---|---|---|
| Nhận diện món ăn | Giữ nutrition scan boundary; candidate chưa ghi meal tới khi user confirm | Input image/preprocess, labels, phần lượng/macros do model hay nguồn dữ liệu nào tính |
| AI Companion | Owner thread/history + inference boundary, không fake response | Model/chat runtime, tokenizer, context limits, output và streaming có hay không |
| Gợi ý/điều chỉnh bài tập | Explicit suggestion operation, user accept rồi cập nhật plan theo quyền | Input profile/plan/history, output schema và giới hạn chuyên môn |
| AI hỗ trợ khác trong prototype | Giữ inventory và capability disabled | Team AI xác nhận từng mục; không tự bật moderation hoặc auto-ban |
POST workout session chỉ lưu kết quả; không tự gắn inference vào mọi lần log session.
Một hoặc nhiều .pt đều có thể được giao; từng capability có readiness riêng. Không ép một file phải đáp ứng mọi tính năng.

## 3. Làm được ngay trước bàn giao
1. Kiểm interface/adapter hiện có, tái sử dụng; không tạo runtime-specific abstraction chưa cần.
2. Giữ client-facing contract hiện có trong OpenAPI; ghi field còn chưa xác nhận vào gap, không đổi máy móc schema.
3. Chuẩn bị capability config disabled mặc định; validation, authorization, cancellation và typed unavailable.
4. Viết fixtures có nhãn test-only, tests output validation/error mapping; không fixture fallback production.
5. Chuẩn bị artifact metadata template trong ai-handoff-checklist.md; path qua cấu hình ngoài repo/image source, không hardcode tên model.
6. Ghi timeout/concurrency/body/output limits là configurable và cần đo; không chốt20 concurrent hoặc GPU/CPU khi chưa biết model.
Không cần file .pt để hoàn thành các bước này; không đánh tích hợp model VERIFIED vì chỉ có adapter.

## 4. Contract cần chốt sau nhận file
- Version/schema từng capability, input dimensions/units/normalization, output labels/units và optional fields.
- Confidence chỉ dùng khi capability có metric thực với semantics được nhóm AI giải thích. Không bắt chat trả confidence giả và không suy0 khi thiếu.
- Nhận diện tên món không đồng nghĩa biết khẩu phần/calories/macros. Trường chưa được hỗ trợ phải unavailable/null theo contract đã thống nhất, không bịa.
- Meal: kết quả candidate/estimate; user confirm mới lưu. Workout: suggestion không tự thay plan. Chat: message/history theo retention của chat, không áp quy tắc meal-confirm cho mọi output.
- Chọn sync/async/streaming sau đo artifact; nếu đổi client contract phải qua BE-801 và cập nhật hai FE, không silently đổi HTTP200 thành job.
- Nội bộ .NET ↔ inference transport chưa chốt. Service Python riêng là phương án khảo sát, KHÔNG là kiến trúc bắt buộc đã quyết.
- Không convert ONNX/TorchScript hoặc đổi định dạng khi chưa xác nhận compatibility/chất lượng với nhóm AI.

## 5. Lỗi và quyền
Mục tiêu codes: AI.UNAVAILABLE (503), AI.INVALID_OUTPUT (502), AI.RATE_LIMITED (429 + Retry-After), theo namespace FITMATE trong spec khi implement.
Không tự tạo quota402/token billing khi chưa có business về entitlement.
Disabled/thiếu artifact/runtime lỗi → unavailable trung thực; không generic chatbot response để che lỗi.
AI không quyết role/verified/giá/payment/refund/booking state; output không được thực thi như command.
Không log ảnh, health/chat payload hoặc secrets; minimize data đưa vào inference.

## 6. Khi nhận .pt
Theo ai-handoff-checklist: kiểm nguồn/checksum/code inference, chạy thử trong môi trường cô lập sau khi được phép, đo tài nguyên và đối chiếu output mẫu.
Chỉ sau đó chốt adapter/deployment/runtime và cập nhật plan. Không load artifact chưa rõ nguồn trực tiếp trong API production.
Nếu đang áp dụng chế độ user tự chạy lệnh: agent chỉ viết code và đưa lệnh; user chạy inference/build/tests và cung cấp output.

## 7. Readiness
PREPARED: boundary/config/errors/tests đã viết; tests chưa chạy vẫn pending verification.
RECEIVED: nhận artifact và metadata, chưa có nghĩa load được.
VALIDATED: thực thi mẫu, schema/quality/resource checks đạt với evidence.
ENABLED: integration end-to-end, consent/privacy/config và operator release gate đạt.
Không tác động booking/payment/manual workout/nutrition chỉ vì AI chưa sẵn sàng.
