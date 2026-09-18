# VNPay tích hợp — Điều kiện sản phẩm và Google Play policy

> Viết 2026-09-18. Tra cứu Google Play policy hiện hành. **Cần verify lại** với policy cập nhật mới nhất trước khi launch.

## 1. Tổng quan sản phẩm Fitmate

Fitmate là **nền tảng đặt lịch PT (Personal Trainer)**. Có 3 loại nội dung/sản phẩm:

| Loại | Ví dụ | Mô tả | Loại sản phẩm theo Play policy |
|---|---|---|---|
| **Đặt lịch PT** | Member book buổi tập với PT | Dịch vụ thật ngoài đời (offline 1-1 tại phòng gym). Không phải nội dung số. | **Dịch vụ vật lý** (physical service) — KHÔNG qua Play Billing |
| **Nội dung số (UGC)** | Post kiến thức PT, comment, reaction | Community UGC, có thể có ads/recommendation. | UGC phải tuân `User-Generated Content` policy + moderation |
| **Gói premium/AI** | (CHƯA CÓ trong sản phẩm hiện tại) | Subscription unlock content / AI features. | Nếu có: thuộc **In-app Purchase** của Google Play |

## 2. Chính sách Google Play — Áp dụng cho Fitmate

> Tham khảo [Google Play Developer Policy Center](https://support.google.com/googleplay/android-developer) — kiểm tra lại khi submit.

### 2.1 Dịch vụ vật lý (Physical Goods/Services) — Đặt lịch PT

> Google Play **KHÔNG yêu cầu** dùng Play Billing cho:
> - Mua hàng hóa vật lý (physical goods).
> - Dịch vụ thực hiện ngoài app (real-world services performed outside the app).
> - Vé sự kiện offline.
> - Quyên góp từ thiện.
> - Đặt phòng khách sạn / vé máy bay.

**Fitmate rơi vào trường hợp "dịch vụ thực hiện ngoài app"**: PT và member gặp nhau tại phòng gym, không có digital content unlock. **VNPay là được phép**, không vi phạm Play Billing policy.

Tuy nhiên: Nếu sau này Fitmate thêm tính năng "premium content unlock" (xem video workout của PT chỉ với subscription), cái đó PHẢI qua Play Billing.

### 2.2 UGC (User-Generated Content)

Play yêu cầu:
- Có hệ thống báo cáo / flag nội dung vi phạm.
- Có cơ chế ẩn/xóa nội dung bị flag trong vòng 24h khi có yêu cầu hợp lệ.
- Có điều khoản cấm nội dung bất hợp pháp, xúc phạm, quảng cáo chất cấm.
- Account bị vi phạm nhiều lần phải bị suspend.

**Fitmate đã có**: `POST /reports`, `POST /admin/reports/{id}/resolve|dismiss`, `POST /admin/users/{id}/lock` → đủ cho Yêu cầu này. Cần đảm bảo:
- User bị report ≥ N lần có thể auto-hide post hoặc auto-lock.
- Tài khoản `usr-003` (Vũ Đức Thịnh, spam thuốc) trong mock data nên bị lock.
- Admin có audit log cho mỗi resolve/dismiss.

### 2.3 Sức khỏe (Health)

App có tính năng workout, dinh dưỡng, chỉ số sức khỏe (height, weight, medical conditions). Play yêu cầu:
- **Health Declaration** nếu app claim "chẩn đoán", "điều trị", hoặc can thiệp y tế.
- **Fitmate KHÔNG claim chẩn đoán/điều trị** — chỉ là "hỗ trợ tập luyện". Nhưng cần khai báo trong store listing là app "lifestyle/fitness", không phải "medical".
- Data Safety form phải khai: thu thập height, weight, allergies, medical conditions → sensitive health data.
- Cần privacy policy URL.
- Cần điều khoản: app không thay thế ý kiến bác sĩ.

### 2.4 Quyền ứng dụng (App Permissions)

- **Camera**: dùng cho meal scan, certificate upload. Cần giải thích trong store listing.
- **Storage / Media**: upload ảnh meal, certificate, post. Hạn chế scope Android 13+.
- **Location**: KHÔNG cần (booking là manual chọn phòng gym).
- **Notifications**: cần cho booking reminder. Foreground service CHỈ dùng khi có lý do rõ ràng (workout timer); nếu chỉ push notification thì không cần.
- **Internet**: cần cho mọi thứ.
- **Body sensors / Activity recognition**: KHÔNG dùng (Fitmate không tracking step bằng sensor).

### 2.5 Account deletion

Play yêu cầu app có tài khoản PHẢI cho phép xóa tài khoản (kể từ 2023). Cần:
- Endpoint `DELETE /users/me` (đã có trong OpenAPI).
- Form/account deletion URL accessible from app + store listing.
- Xử lý trong vòng "reasonable time" (Fitmate đề xuất: xóa ngay lập tức + purge sau 30 ngày).
- Confirmation email.
- Không yêu cầu user phải gửi email yêu cầu thủ công.

### 2.6 Data Safety

- **Đã thu thập**: email, full name, height, weight, gender, birthday, medical conditions, allergies, workout logs, meal logs, payment metadata, push device tokens.
- **Chia sẻ với bên thứ ba**: VNPay (payment), SMTP provider (email), push notification provider (FCM), hosting provider.
- **Mã hóa**: TLS in transit, encrypted at rest (managed DB).
- **User có thể yêu cầu xóa**: Có (qua `DELETE /users/me`).
- **Có thể ẩn danh**: Không (Fitmate cần định danh để hoạt động).

## 3. VNPay integration — điều kiện chuyển sang production

### 3.1 Sandbox hiện tại

- Endpoint: `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html`
- TMN: placeholder `__SET_VNPAY_TMN__`
- Hash secret: placeholder `__SET_VNPAY_HASH_SECRET__`
- Return URL: `http://localhost:8080/api/v1/payments/vnpay/return` (dev)
- IPN URL: `https://api.fitmate.vn/api/v1/payments/vnpay/ipn` (production)

### 3.2 Yêu cầu từ VNPay (theo tài liệu merchant)

1. **Merchant account thật**: cần đăng ký qua portal VNPay hoặc đối tác tích hợp. Tốn thời gian duyệt hồ sơ (1-2 tuần).
2. **IPN URL HTTPS bắt buộc**: localhost không nhận IPN. Cần `https://api.fitmate.vn` với TLS hợp lệ.
3. **Hash secret phải được rotate theo định kỳ**: VNPay cho phép rotate, cần update config và redeploy.
4. **Bảng tỷ giá VND**: VNPay chỉ hỗ trợ VND trong production — đã đúng với Fitmate.
5. **Phí giao dịch**: VNPay thu phí merchant theo volume (thường 1.5-3% cho domestic card). **Đây là phí provider, KHÁC với phí nền tảng 10% của Fitmate**. Cần tính vào business model.
6. **Đối soát**: VNPay cung cấp API query transaction để reconcile hàng ngày.
7. **Refund**: VNPay yêu cầu dùng API refund riêng (không tự động). Cần xử lý async.

### 3.3 Tách adapter theo kiến trúc

- `IPaymentProvider` interface (Application layer): `CreatePaymentAsync`, `VerifySignature`, `QueryTransactionAsync`, `RefundAsync`.
- `VnpayPaymentProvider` (Infrastructure): implementation cụ thể.
- Adapter phải **fail-fast khi config sai** trong production env:
  - Nếu `VNPAY_HASH_SECRET` rỗng hoặc = placeholder → fail khi startup.
  - Nếu `VNPAY_PAYMENT_URL` chứa "sandbox" nhưng `ASPNETCORE_ENVIRONMENT=Production` → fail.
- Test double (`FakePaymentProvider`) chỉ dùng ở Development.
- **Production KHÔNG dùng fake**. Adapter fake sẽ throw `NotSupportedException` nếu gặp Production env.

### 3.4 Chống trùng (idempotency)

- Mỗi payment request có `Idempotency-Key` (do client sinh).
- BE lưu `(key, payload_hash, response)` vào `idempotency_records`.
- Nếu retry với cùng key + cùng payload → trả response cũ.
- Nếu retry với cùng key + khác payload → 409 `FITMATE.IDEMPOTENCY.CONFLICT`.
- TTL idempotency record: 24 giờ (theo khuyến nghị VNPay cho retry window).

### 3.5 Xác minh IPN

- IPN endpoint **PHẢI** verify HMAC SHA512 trên toàn bộ query string (theo spec VNPay).
- Phải kiểm tra `vnp_TmnCode` khớp config.
- Phải kiểm tra `vnp_TransactionStatus = "00"` (success).
- Phải idempotent — cùng `vnp_TxnRef` xử lý 1 lần.
- KHÔNG trust redirect từ client (return URL là UX hint, IPN mới là source of truth).
- IPN response: `RspCode=00&Message=OK` nếu xử lý OK; khác nếu fail.

### 3.6 Hoàn tiền (refund)

- Flow: admin gọi `POST /admin/bookings/{id}/refund` với reason + amount.
- BE lưu `Refund` entity (status=Pending), gọi VNPay Refund API async.
- Worker (Outbox) xử lý: gọi provider → update `Refund.Status`.
- Khi provider xác nhận: cập nhật `Payment.RefundedSoFarVnd` + `Appointment.Status = Refunded` + audit log.
- **Giới hạn**: `total_refunded ≤ amount_paid`. Lũy kế nếu refund nhiều lần.

### 3.7 Test trên sandbox

- Tạo booking với payment intent.
- Mở URL VNPay redirect, chọn phương thức test (ATM nội địa, Visa, ...).
- Sau khi thanh toán, VNPay redirect về `return_url` + gọi IPN.
- BE xử lý IPN → cập nhật `Appointment.Status = Paid`.
- Test case:
  - [ ] Thanh toán thành công.
  - [ ] User hủy trước khi thanh toán.
  - [ ] IPN đến trước khi user redirect về.
  - [ ] IPN đến sau khi user redirect về (race condition).
  - [ ] IPN gửi 2 lần (replay).
  - [ ] IPN có signature sai → reject.
  - [ ] User mở return_url trực tiếp với payload giả → không tin.
  - [ ] Refund thành công (full + partial).
  - [ ] Refund vượt quá actual_paid → reject.
  - [ ] Webhook VNPay giả mạo (signature verify fail).

## 4. Điều kiện chuyển từ sandbox sang production merchant

| # | Điều kiện | Trạng thái | Người phụ trách |
|---|---|---|---|
| 1 | Đăng ký merchant VNPay, nhận TMN + hash secret | TODO | User (UA-008) |
| 2 | Whitelist IP server production với VNPay | TODO | User |
| 3 | Cấu hình IPN URL production `https://api.fitmate.vn/api/v1/payments/vnpay/ipn` | TODO | DevOps |
| 4 | TLS certificate hợp lệ cho `api.fitmate.vn` | TODO | User (UA-006) |
| 5 | Test sandbox pass hết 10 test case §3.7 | TODO | Dev |
| 6 | Reconciliation job chạy mỗi giờ, alert mismatch > 0 | TODO | Dev |
| 7 | Refund flow test với sandbox merchant | TODO | Dev |
| 8 | Phí provider VNPay được tính vào business model | TODO | Product / Finance |
| 9 | Audit log cho refund hiển thị trong admin | TODO | Dev |
| 10 | Đăng ký Google Play Console + store listing | TODO | User (UA-010..014) |
| 11 | Data Safety form submit | TODO | User |
| 12 | Account deletion flow test E2E | TODO | Dev + QA |
| 13 | Privacy policy + Terms URL active | TODO | User (UA-012) |
| 14 | Health declaration (nếu claim health benefit) | TODO | Product |

## 5. Rủi ro cần theo dõi

- **R-01**: VNPay thay đổi IPN format/signature → cần update BE + redeploy. Theo dõi release note VNPay.
- **R-02**: Phí provider VNPay tăng → cần update pricing hoặc chuyển provider.
- **R-03**: User khiếu nại chargeback → admin cần flow refund + audit log.
- **R-04**: Google Play update policy về UGC / Health → có thể phải update app.
- **R-05**: Play policy cấm một loại nội dung nào đó (vd: health claim) → phải gỡ hoặc rephrase.

## 6. Việc cần làm

- User trả lời các câu hỏi UA-008..UA-014 trong `user-actions.md`.
- Verify Google Play policy mới nhất trước khi submit (tra cứu lại `support.google.com/googleplay/android-developer`).
- Sau khi có merchant VNPay thật: chạy full test §3.7 trên sandbox trước.
- Update `release-checklist.md` gate "Payment production-ready" + "Google Play submission prerequisites".

## 7. Nguồn

- Google Play Developer Policy Center: https://support.google.com/googleplay/android-developer
- VNPay Merchant docs: https://sandbox.vnpayment.vn/apis/
- Tra cứu: 2026-09-18. **Verify lại trước khi submit** vì policy có thể đổi.
