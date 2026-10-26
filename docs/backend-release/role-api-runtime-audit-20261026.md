# Rà soát dữ liệu thật theo vai trò — 2026-10-26

> Cập nhật 27/10: các mục "chưa có code" về native FCM, AI hosted và số liệu hồ sơ bên dưới là lịch sử. Đã bổ sung implementation/API thật, dọn prototype và kiểm thử unit backend; xem [cấu hình và UAT hiện hành](provider-setup-and-real-tests-20261027.md). Key/console setup, PostgreSQL integration, native UI và end-to-end vẫn chưa nghiệm thu.

Trạng thái: IMPLEMENTED_UNVERIFIED cho lượt UI mới. Không phải chứng nhận toàn bộ chức năng hoặc cho phép phát hành. Yêu cầu owner: commit công việc đã làm, lưu thiếu cấu hình, rà Admin/member/PT; loại dữ liệu giả trong runtime, kết nối API thật.

## Đã sửa trong lượt rà soát

| Vai trò | Sai lệch thực tế | Thay đổi |
|---|---|---|
| PT | Buổi dạy chỉ dẫn tới buổi đầu tiên; không có danh sách đầy đủ | Thêm `/pt/sessions`, lấy `/me/trainer-bookings`, lọc trạng thái và phân trang server, vào chi tiết từng buổi |
| PT | Tổng quan tự xác nhận PT đã kiểm định; nhiệm vụ rỗng cố định; đổi trạng thái chỉ thông báo | Lấy public profile/chứng chỉ/dịch vụ/lịch Pending thật; nhiệm vụ điều hướng tới lịch hoặc quản lý dịch vụ; không suy active service thành đủ điều kiện nhận lịch |
| PT | Doanh thu thiếu trang sau; trạng thái dùng substring `paid`; net bị gọi là đã nhận tiền | Dùng trạng thái settlement chính xác; phân trang, kỳ tổng quan 7/30/90/all; tách tiền đã chi/chờ chi từ API. Không tự suy tiền hoàn từ gross-fee-net |
| Member | PT thiếu giá/kinh nghiệm được gán miễn phí/0 năm, rating lỗi thành 5 sao | Giữ null rõ ràng; kiểm tra số liệu wire; từ chối dữ liệu malformed thay vì bịa giá/đánh giá |
| Member/PT | Lỗi lấy số liệu hub biến thành 0 | Chỉ hiển thị số lịch khi đã có trang đầy đủ; lỗi/thiếu trang hiển thị chưa có số liệu |
| Cộng đồng | Hồ sơ hiển thị 3 huy hiệu cố định và bộ đếm 0 không có nguồn | Bỏ huy hiệu giả; số chưa có hiện dấu —; bài viết theo hồ sơ báo thiếu khả năng, không nói user không có bài |
| Admin | Chi tiết account dùng snapshot của bảng | GET `/admin/users/{id}` khi mở; loading/error/retry; không hiện snapshot như chi tiết mới |

Các API trên đã tồn tại; lượt này không thay backend runtime, schema hoặc archive triển khai đã staging trước đó. Fixtures trong thư mục test vẫn phục vụ kiểm thử; không phải dữ liệu runtime. Static search ở mobile/app + mobile/src (loại test) và Admin src (loại test) không tìm importer mockData/localDb/fixture runtime, ngoài comment nói không fallback. Điều này không chứng minh không còn mọi literal giả trong toàn bộ sản phẩm.

## Còn mở — phải tiếp tục, không đánh DONE

- Author-filtered posts đã được bổ sung ở lượt tiếp theo: profile dùng API thật + server cursor/Xem thêm, sửa Friends visibility và mất độ chính xác cursor; commit backend `faabea1`, mobile `26f77fc`. Public counters bạn bè/sức khỏe và earned badges vẫn chưa có contract/consent/rules hoàn chỉnh; không công khai dữ liệu sức khỏe riêng tư để lấp chỗ trống.
- Native push chưa có provider/token registration/lifecycle; OS permission không chứng minh gửi được. Cần triển khai trước khi bật capability.
- Tra cứu tên chủ tài khoản cần BankLookup ClientId/ApiKey; không thay bằng tên người dùng tự đoán. Không yêu cầu SMS khi lưu STK.
- Meet thiếu ClientId/ClientSecret/RedirectUri. payOS thiếu ClientId/ApiKey/ChecksumKey/AccountNumber/ReturnUrl/CancelUrl. Play Billing thiếu ServiceAccountJsonBase64. Chưa bật bán gói/thanh toán khi thiếu cấu hình và test.
- Groq/SMTP/GA4/SMS token đã có cấu hình presence-only; không có evidence end-to-end cho tất cả luồng. Food scan/workout AI cần model service và internal token; Gemini/R2 chỉ cần nếu lựa chọn provider đó.
- Permanent deletion finalizer vẫn tắt theo gate retention/backup. Không bật vì yêu cầu bỏ mock.
- Kiểm tra UI Android: sessions nhiều trang, Pending→chi tiết→quay lại tự refresh; revenue nhiều trang/kỳ, on_hold/approved/paid; giá null vs miễn phí thật; hub API lỗi; hồ sơ chưa có counters.
- Admin: mở detail khi list cũ; 404/503; đóng/mở user khác khi request đang chạy; lock/unlock refetch; kiểm các module certificate/booking/finance/support/audit bằng tài khoản và backend test thật.

## Kiểm chứng

Đã review source/caller/contract và `git diff --check`. Không chạy mobile/Admin test/typecheck/build theo manual-execution instructions. Có source tests cho trainer wire integrity và Admin detail mới/lỗi; tests dùng fixtures để mô phỏng lỗi, không thay API trong sản phẩm.

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_FE\mobile
npm run type-check
npm test
Set-Location D:\Ky8\exe201\FitmateWebApp
npm run typecheck
npm run test:run
npm run build
```

Lượt tiếp theo: backend full unit 1028/1028 pass sau cursor/author filter; cuối lượt gồm refund adjustment là 1029/1029 pass. Integration project compile cuối lượt 0 warnings/errors, PostgreSQL tests pending do local port 5432 đóng. Không là evidence cho UI mới. Account hub hiện lấy tổng server từng trạng thái thay giới hạn 100 lịch. API doanh thu bổ sung `refund_adjustment_vnd`, phần giảm thu nhập PT từ hoàn tiền của các buổi đã ghi nhận trong kỳ; app chỉ hiển thị field thật, server cũ vẫn hiện chưa có số liệu. Không đồng nhất với toàn bộ tiền đã hoàn cho khách.

VPS updater vẫn OPERATOR_PENDING. Source allowlist/filter đã cập nhật nhưng file tar đã upload trước đó là cũ: cần chạy lại prepare-mobile-update.ps1 sau commit rồi mới dùng lệnh deploy operator. Không khẳng định backend đã deploy. Mobile cần build mới sau khi sửa/kiểm chứng hoàn tất, không push bản đang rà lên Play.
