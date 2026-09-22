# VNPay + Google Play policy mapping cho Fitmate

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> Viết 2026-09-18. **Cập nhật 2026-09-18 (phiên 7)** theo DEC-031 / UA-024 chi tiết.
>
> Phạm vi file: **rà soát tài liệu + mapping từ chính sách → quyết định kỹ thuật Fitmate**. KHÔNG đồng nghĩa "Google Play compliant". Việc khai báo trên Console là **action riêng** (UA-010..014, xem `user-actions.md`).
>
> Ngày kiểm tra: 2026-09-18. **Cần verify lại** với policy cập nhật mới nhất trước ngày submit.

## 1. Nguồn chính thức đã đối chiếu

- https://support.google.com/googleplay/android-developer/answer/10281818 — Payments (Play Billing).
- https://support.google.com/googleplay/android-developer/answer/14738291 — Health apps & Health declaration.
- https://support.google.com/googleplay/android-developer/answer/9876937 — User-Generated Content.
- https://support.google.com/googleplay/android-developer/answer/13327111 — Account deletion.
- https://support.google.com/googleplay/android-developer/answer/10787469 — Data Safety form.

Các câu dưới đây phải đối chiếu với nguồn chính thức tại thời điểm submit. Trích dẫn ngắn dưới đây chỉ mô tả nội dung tài liệu, không thay thế policy bằng văn bản.

## 2. Tổng quan sản phẩm Fitmate

Fitmate là **nền tảng đặt lịch PT (Personal Trainer)**. Hiện có 3 nhóm sản phẩm/nội dung (có thể mở rộng):

| Nhóm | Ví dụ | Loại sản phẩm | Phân loại Play policy |
|---|---|---|---|
| Đặt lịch PT | Member book buổi tập với PT, gặp trực tiếp | Dịch vụ vật lý | KHÔNG bắt buộc Play Billing |
| Đặt lịch PT online 1:1 | Member book buổi PT qua video call | Dịch vụ giữa hai cá nhân | Có thể vẫn miễn Play Billing nếu đủ điều kiện (xem §3) |
| UGC | Post kiến thức, comment, reaction | User-generated content | Tuân UGC policy (§4) |
| Health/fitness data | Height, weight, meal log, workout log | Dữ liệu sức khỏe (chưa claim medical) | Lifestyle/Fitness — Health declaration (§5) |
| (Tương lai) AI / video / premium content | AI coach, video library, subscription | Nội dung số | BẮT BUỘC Play Billing (§6) |

**Quan trọng**: Phân loại theo sản phẩm thực tế trước khi dùng VNPay. Không tuyên bố VNPay dùng được cho mọi sản phẩm Fitmate.

## 3. PAYMENTS

### 3.1 Đặt lịch PT gặp trực tiếp (offline)

- Theo nguồn Payments chính thức: Google **KHÔNG yêu cầu** dùng Play Billing cho dịch vụ thực hiện ngoài app (real-world services performed outside the app). Đặt lịch PT tại phòng gym là dịch vụ vật lý — **VNPay OK** ở giai đoạn này.
- Số tiền thu qua VNPay = phí dịch vụ (booking), có thể có split platform fee / PT share theo domain-rules.

### 3.2 Đặt lịch PT online 1:1 (video call / nhắn tin)

- **KHÔNG được mặc định giống offline**. Cần đọc ngoại lệ của Google: giao dịch giữa **hai cá nhân** (peer-to-peer) **và** không có bản ghi phát lại trong app.
- Nếu Fitmate có:
  - Recording buổi tập lưu trong app, hoặc
  - Video on-demand của PT trong app, hoặc
  - AI-generated content mới,

  → cần đánh giá Play Billing.
- **Chưa có PT online 1:1 trong release đầu** (xem `docs/mobile-redesign/`). Khi thêm vào, **mở task riêng** review lại policy Play Billing trước khi ship.

### 3.3 Tương lai: AI / video / premium content

- Gói subscription unlock AI features hoặc video content trong app → **BẮT BUỘC** dùng Play Billing.
- Không thu tiền qua VNPay cho nội dung số trong app.

### 3.4 Provider interface & sandbox

- Backend giữ `IPaymentProvider` interface (xem `api-contract.md §16`). Release đầu chỉ có 1 implementation: `VnpayPaymentProvider`.
- Adapter fail-fast ở startup nếu config sai ở production env:
  - `VNPAY_HASH_SECRET` rỗng hoặc = placeholder → fail.
  - `VNPAY_PAYMENT_URL` chứa "sandbox" nhưng `ASPNETCORE_ENVIRONMENT=Production` → fail.
- Fake payment chỉ dùng ở Development. Production KHÔNG fallback. Xem `decisions.md` DEC-033.
- Idempotency-Key bắt buộc cho mọi mutation payment. Xem `api-contract.md §11`.
- Webhook IPN verify HMAC + idempotency + accept out-of-order đến khi terminal. Xem `domain-rules.md §3.6`.

## 4. UGC (User-Generated Content)

- App có post, comment, reaction, friend request, chat friend, chat AI.
- Theo nguồn UGC chính thức: cần
  - Terms / usage policy phù hợp (UA-012).
  - Hệ thống moderation có truy cập được (không chỉ có endpoint).
  - Báo cáo nội dung/người dùng (`POST /reports`) — hiện thực theo DEC-028.
  - Block user theo trường hợp áp dụng (`POST /admin/users/{id}/lock`).
- **Có endpoint trong OpenAPI KHÔNG đồng nghĩa đã đáp ứng**. Phải có implementation + luồng người dùng thực sự truy cập được (UI report button, admin dashboard list + resolve, audit log).
- **Không khẳng định** "Google bắt buộc xử lý mọi report trong 24 giờ" — policy thường yêu cầu "in a reasonable time" / phù hợp từng khu vực. Nếu Fitmate chọn SLA 24h nội bộ → ghi đó là **chính sách Fitmate**, không trích dẫn Google.
- **Không** dùng auto-block theo N reports như quy tắc của Google. Admin resolve từng case theo `decisions.md` DEC-028. Có thể xét auto-hide nội dung nếu report count vượt ngưỡng — đó là feature Fitmate tự thiết kế, không phải yêu cầu Play.

## 5. HEALTH (Health apps & Health declaration)

### 5.1 Điều khoản Health declaration

- App có dữ liệu sức khỏe: height, weight, medical conditions, allergies, meal logs, workout logs.
- Theo Health apps policy: app thuộc loại Health declaration **KHÔNG chỉ** dựa trên việc *claim* "chẩn đoán / điều trị". App xử lý **personal health & fitness data** cũng có thể cần Health declaration. Cần đọc nguồn chính thức để xác định ngoại lệ cụ thể (vd: app chỉ fitness không claim medical).
- **Không khẳng định** "chỉ app chẩn đoán/điều trị mới cần Health declaration" — đó là hiểu sai policy.

### 5.2 Khai báo đúng chức năng

- Fitmate khai:
  - **Lifestyle / Fitness**, **KHÔNG** claim chẩn đoán / điều trị y tế.
  - **Nutrition**: tracking + planning, không phải medical nutrition therapy.
  - **Workout**: hỗ trợ tập luyện, không phải physical therapy.
- **Không khai `medical functionality`** nếu sản phẩm không có. Nếu sau này thêm tính năng liên quan y tế → review Health declaration lại trước khi ship.
- Privacy policy bắt buộc (UA-012), đặc biệt cho dữ liệu nhạy cảm (sensitive health data).

## 6. ACCOUNT DELETION

- Theo Account deletion policy: app có tài khoản PHẢI cho phép user xóa tài khoản.
- `DELETE /api/v1/users/me` đã trong OpenAPI (FP-ID-11). Cần implementation + UI trong app + landing page công khai (user truy cập được mà không cần login).
- Xác định rõ:
  - **Dữ liệu xóa** vs **ẩn danh** vs **giữ lại** cho finance / audit. Lý do giữ lại phải hợp lệ (vd: ledger, tax).
  - **Thời gian xử lý**: ghi rõ trong policy Fitmate (không bịa rằng Google bắt buộc một mốc cụ thể).
  - **Email confirmation**: ghi rõ nếu Fitmate yêu cầu — đây là chính sách Fitmate, không phải yêu cầu Google.
  - **Purge sau N ngày**: ghi rõ trong policy Fitmate.

## 7. DATA SAFETY

- App khai trên Play Console: `Data Safety` form.
- **Đối chiếu với code/SDK/data flow thật**, không copy template.
- Theo nguồn Data Safety chính thức: collection / sharing có định nghĩa + ngoại lệ riêng; **không mặc định** mọi truyền dữ liệu cho service provider đều khai giống nhau.
- Cần khai:
  - **Collection**: email, full name, phone, height, weight, gender, birthday, medical conditions, allergies, workout logs, meal logs, payment metadata, push device tokens.
  - **Sharing**: VNPay (payment), SMTP (email), push provider (FCM), hosting/DO (DB at-rest, TLS in transit).
  - **Security**: TLS in transit, encryption at rest.
  - **User controls**: xóa tài khoản qua `DELETE /users/me`.
  - **Anonymization**: Fitmate KHÔNG ẩn danh (cần định danh để hoạt động).
- **Không đánh dấu hoàn tất form** khi chưa thao tác trên Console.

## 8. Việc cần làm

> Phần này tổng hợp các action còn lại. Tránh nhầm với phần "rà soát tài liệu" đã DONE.

### 8.1 User actions (UA)

- UA-012: Privacy policy + Terms URL.
- UA-013: Khai Data Safety form trên Console.
- UA-014: Verify account deletion flow end-to-end (test `DELETE /users/me`).
- UA-024: Verify lại policy Play ngay trước ngày submit.

### 8.2 Out-of-scope / post-release (ghi TODO)

- Khi thêm PT online 1:1 / video on-demand / AI subscription → review Play Billing §3.
- Khi thêm tính năng claim medical → review Health declaration §5.
- Khi phát triển auto-hide post sau N reports → chỉ là feature Fitmate, không gọi là yêu cầu Google.

## 9. Quan trọng — không ghi "compliant"

- **KHÔNG** viết trong bất kỳ tài liệu / commit message / PR description rằng "đã Google Play compliant" chỉ từ việc đọc chính sách.
- Verify thực sự trên Console và test app end-to-end trên internal-testing track **trước** ngày submit production.
- Mỗi lần cập nhật policy Google, ghi ngày kiểm tra mới vào đầu file.

## 10. Nguồn

- Google Play Developer Policy Center: https://support.google.com/googleplay/android-developer
- Payment policy detail: https://support.google.com/googleplay/android-developer/answer/10281818
- Health apps: https://support.google.com/googleplay/android-developer/answer/14738291
- UGC: https://support.google.com/googleplay/android-developer/answer/9876937
- Account deletion: https://support.google.com/googleplay/android-developer/answer/13327111
- Data Safety: https://support.google.com/googleplay/android-developer/answer/10787469
- VNPay sandbox docs: https://sandbox.vnpayment.vn/apis/

Lần kiểm tra: **2026-09-18 (phiên 7)**.
