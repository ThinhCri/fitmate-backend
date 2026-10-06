# Các luồng nghiệp vụ chính của Fitmate

Tài liệu này giải thích cách các chức năng chính phối hợp giữa Mobile App, Admin Web và Backend.

- Backend là nguồn chuẩn về quyền, trạng thái, tiền và thời gian.
- Mobile App phục vụ Hội viên và Huấn luyện viên cá nhân (PT).
- Admin Web chỉ phục vụ Quản trị viên.
- OpenAPI chuẩn nằm tại `docs/backend-release/openapi/fitmate-v1.yaml`.
- Khi tài liệu này khác code hoặc OpenAPI đã được kiểm chứng mới hơn, phải kiểm tra và đồng bộ lại trước khi sửa frontend.

## 1. Vai trò người dùng

### Hội viên

Hội viên có thể:

- Quản lý hồ sơ cá nhân, sức khỏe, tập luyện và dinh dưỡng của mình.
- Tìm PT đã được duyệt và đang hoạt động.
- Xem dịch vụ và lịch trống do PT công bố.
- Đặt lịch, thanh toán, hủy theo chính sách và xác nhận kết quả buổi tập.
- Khiếu nại nếu PT báo hoàn thành nhưng buổi tập có vấn đề.

### Huấn luyện viên cá nhân

PT có toàn bộ quyền của Hội viên trên dữ liệu cá nhân, đồng thời có thể:

- Nộp hồ sơ chuyên môn và chứng chỉ để đăng ký làm PT.
- Tạo dịch vụ và công bố lịch trống sau khi được duyệt.
- Xem lịch được đặt, bắt đầu và gửi kết quả hoàn thành buổi tập.
- Theo dõi doanh thu, hoàn tiền và trạng thái chi trả của mình.

### Quản trị viên

Quản trị viên có thể:

- Duyệt hoặc từ chối hồ sơ PT dựa trên thông tin và chứng chỉ.
- Quản lý người dùng, booking, báo cáo, tranh chấp và tài chính trong phạm vi được cấp quyền.
- Duyệt khoản chi cho PT và ghi nhận giao dịch sau khi đã chuyển khoản thật.

Quản trị viên không mặc định được đọc dữ liệu sức khỏe hoặc tin nhắn riêng tư của người dùng.

## 2. Đăng ký tài khoản Hội viên

### Luồng bình thường

1. Người dùng nhập email, mật khẩu và thông tin đăng ký.
2. Backend tạo tài khoản ở trạng thái chưa xác minh email.
3. Backend gửi mã OTP qua email.
4. Người dùng nhập đúng OTP còn hiệu lực.
5. Backend đánh dấu email đã xác minh.
6. Người dùng đăng nhập và sử dụng ứng dụng.

### Người dùng thoát trước khi nhập OTP

Tài khoản vẫn tồn tại nhưng chưa được xác minh.

Khi người dùng đăng nhập lại:

1. Backend không tạo phiên đăng nhập hoàn chỉnh.
2. Backend trả trạng thái yêu cầu xác minh email.
3. Mobile App mở lại màn hình OTP.
4. Người dùng có thể yêu cầu gửi lại mã theo giới hạn chống spam.

Không yêu cầu người dùng đăng ký lại bằng cùng email.

### Đăng nhập bằng email chưa đăng ký

Backend không gửi OTP và không tiết lộ chi tiết nội bộ. Đăng nhập thất bại với thông báo tiếng Việt phù hợp cho người dùng.

## 3. Đăng ký và duyệt tài khoản PT

### PT nộp hồ sơ

1. Người dùng chọn đăng ký vai trò PT.
2. Người dùng nhập thông tin chuyên môn, kinh nghiệm và specialty.
3. Người dùng tải ảnh hoặc tài liệu chứng chỉ lên hệ thống.
4. File chứng chỉ được lưu dưới dạng riêng tư; đường dẫn tạm chỉ được cấp sau khi kiểm tra quyền.
5. Người dùng gửi hồ sơ xét duyệt.
6. Backend chuyển hồ sơ sang trạng thái chờ duyệt.
7. Ứng dụng thông báo rõ rằng tài khoản phải chờ Quản trị viên kiểm tra.

### Admin duyệt hồ sơ

1. Admin mở danh sách hồ sơ PT đang chờ duyệt.
2. Admin xem thông tin chuyên môn và ảnh chứng chỉ.
3. Admin tự kiểm tra tính hợp lệ của chứng chỉ bằng quy trình vận hành bên ngoài nếu cần.
4. Admin chọn duyệt hoặc từ chối và phải nhập lý do khi từ chối.
5. Backend ghi audit và gửi thông báo kết quả cho PT.

### Kết quả

- Được duyệt: tài khoản PT được hoạt động và có thể tạo dịch vụ/lịch trống.
- Đang chờ: không được công bố dịch vụ có thể đặt lịch.
- Bị từ chối: không được hoạt động như PT; người dùng đọc được lý do và làm theo chính sách nộp lại hồ sơ.
- Có dấu hiệu giả mạo: Admin có thể khóa tài khoản theo quy trình quản lý người dùng.

## 4. PT tạo dịch vụ và lịch trống

1. PT đã được duyệt tạo dịch vụ, giá và thời lượng.
2. Tiền luôn là số nguyên Việt Nam đồng.
3. PT công bố các slot có thể nhận khách.
4. Việc công bố slot được xem là PT đã chấp thuận trước việc nhận booking tại thời điểm đó.
5. Backend kiểm tra xung đột lịch trước khi cho phép công bố hoặc đặt slot.

Hội viên chỉ nhìn thấy PT, dịch vụ và slot đủ điều kiện hoạt động.

## 5. Hội viên đặt lịch và thanh toán

### Giữ slot

1. Hội viên chọn PT, dịch vụ và slot.
2. Backend kiểm tra slot còn trống và tạo booking hold.
3. Hold có hiệu lực trong 10 phút.
4. Trong thời gian này, người khác không thể chiếm cùng slot.
5. Hết 10 phút mà không có thanh toán hợp lệ, hold chuyển sang hết hạn và slot được giải phóng.

### Thanh toán

1. Mobile App yêu cầu Backend tạo giao dịch PayOS cho hold.
2. Backend trả thông tin thanh toán do PayOS cung cấp.
3. Hội viên hoàn tất thanh toán trên luồng PayOS.
4. Backend chỉ tin webhook có chữ ký hợp lệ, đúng merchant, đúng booking và đúng số tiền.
5. Redirect về Mobile App chỉ dùng để hiển thị kết quả tạm thời, không được dùng để tự đánh dấu đã trả tiền.
6. Khi webhook được xác minh, Payment chuyển sang `Paid` và Appointment chuyển sang `Confirmed`.

PT không cần bấm duyệt lại vì slot đã được PT công bố trước đó.

### Webhook đến muộn

Nếu tiền được xác minh sau khi hold đã hết hạn và slot không còn thuộc booking đó, Backend phải ghi nhận khoản tiền đã nhận nhưng không được tự chiếm lại slot. Trường hợp này chuyển sang xử lý hoàn tiền/đối soát.

## 6. Trạng thái booking

| Trạng thái | Ý nghĩa |
|---|---|
| `Held` | Slot đang được giữ tối đa 10 phút để thanh toán. |
| `Pending` | Trạng thái chuyển tiếp hoặc dữ liệu tương thích cũ; không được frontend tự suy ra đã xác nhận. |
| `Confirmed` | Thanh toán đã được xác minh và lịch đã được xác nhận. |
| `InProgress` | PT đã bắt đầu buổi tập. |
| `AwaitingMemberConfirmation` | PT đã gửi kết quả hoàn thành, đang chờ Hội viên xác nhận hoặc khiếu nại. |
| `Completed` | Buổi tập đã được Hội viên xác nhận hoặc tự xác nhận sau thời hạn. |
| `Disputed` | Hội viên đã báo vấn đề; chờ Admin xử lý. |
| `Cancelled` | Booking đã bị hủy. |
| `Expired` | Hold hoặc thời hạn chấp thuận đã hết. |
| `NoShow` | Trạng thái dành cho trường hợp vắng mặt sau khi có quy trình xác minh phù hợp. |

Không ánh xạ trạng thái theo vị trí mảng riêng ở frontend. Phải dùng đúng giá trị wire contract.

## 7. Bắt đầu và hoàn thành buổi tập

### Bắt đầu

1. Chỉ PT được gán cho booking mới được bắt đầu buổi tập.
2. Booking phải đang ở trạng thái `Confirmed`.
3. Backend kiểm tra giới hạn thời gian bắt đầu theo domain rule.
4. Booking chuyển sang `InProgress`.

### PT gửi hoàn thành

1. Sau khi buổi tập kết thúc, PT bấm hoàn thành.
2. Backend không chuyển ngay sang `Completed`.
3. Booking chuyển sang `AwaitingMemberConfirmation`.
4. Backend lưu thời điểm PT gửi hoàn thành.
5. Backend đặt thời hạn phản hồi của Hội viên là 24 giờ.

### Hội viên xác nhận

1. Hội viên mở chi tiết booking.
2. Hội viên chọn xác nhận buổi tập đã hoàn thành.
3. Booking chuyển sang `Completed`.
4. Backend ghi nhận thời điểm hoàn thành cuối cùng.
5. Doanh thu và khoản phải trả cho PT mới được ghi vào sổ cái tài chính.

### Hội viên không phản hồi

Sau 24 giờ kể từ khi PT gửi hoàn thành:

1. Background service tìm các booking đã hết hạn phản hồi.
2. Nếu booking chưa có tranh chấp, hệ thống tự xác nhận.
3. Booking chuyển sang `Completed`.
4. Doanh thu được ghi nhận giống trường hợp Hội viên xác nhận thủ công.

### Hội viên khiếu nại

1. Trong thời hạn 24 giờ, Hội viên chọn báo vấn đề.
2. Hội viên cung cấp lý do.
3. Booking chuyển sang `Disputed`.
4. Booking không được tự hoàn thành và không đủ điều kiện payout.
5. Admin xử lý dựa trên evidence và audit, không tự ý sửa trạng thái thiếu căn cứ.

## 8. Chính sách hủy lịch 24 giờ

### Hội viên tự hủy

Hội viên chỉ được tự hủy khi thời điểm hủy cách giờ bắt đầu ít nhất 24 giờ.

Ví dụ:

- Lịch bắt đầu lúc 18:00 ngày 10/10.
- Hội viên tự hủy trước hoặc đúng 18:00 ngày 09/10: được phép.
- Hội viên hủy sau 18:00 ngày 09/10: API trả conflict và ứng dụng hướng dẫn liên hệ hỗ trợ.

Backend tính quy tắc bằng thời gian UTC; frontend chỉ định dạng sang múi giờ hiển thị.

### PT hủy

PT hủy theo chính sách vận hành. Việc hủy của PT phải tạo thông báo và được xem xét hoàn tiền cho Hội viên. Không dùng giới hạn tự hủy 24 giờ của Hội viên để che lỗi hoặc trách nhiệm của PT.

### Hủy không đồng nghĩa đã hoàn tiền

Trạng thái booking và trạng thái payment là hai trạng thái độc lập:

- Booking có thể đã `Cancelled` nhưng Refund vẫn `Pending`.
- Chỉ khi có chứng từ và đối soát thành công, Refund mới chuyển sang `Succeeded`.
- Tổng tiền hoàn nhiều lần không được vượt quá số tiền thực trả.

## 9. Ghi nhận doanh thu

Backend chỉ ghi nhận doanh thu khi booking đã `Completed`.

Không ghi nhận doanh thu khi booking chỉ đang:

- `Pending`;
- `Confirmed`;
- `InProgress`;
- `AwaitingMemberConfirmation`;
- `Disputed`.

Với booking hợp lệ:

- Tổng tiền thực trả là gross booking fee.
- Phí nền tảng hiện tại là 10% theo snapshot chính sách của booking.
- Phần PT là 90% còn lại.
- Phép tính và làm tròn được thực hiện tại Backend, không để frontend tự tính số tiền quyết toán.

## 10. Chi trả cho PT

Fitmate hiện dùng quy trình chi trả thủ công, không tự động chuyển tiền.

### Điều kiện tối thiểu

Một booking chỉ có thể được xét chi khi:

- Booking đã `Completed`.
- Đã qua ít nhất 24 giờ kể từ `CompletedAtUtc`.
- Payment đã nhận tiền hợp lệ.
- Không có refund đang chờ.
- Không có tranh chấp mở.
- Số tiền, phí nền tảng và phần PT khớp sổ cái.
- Admin đã thực hiện bước xác thực tài chính tăng cường.

### Quy trình

1. Admin kiểm tra xác nhận của khách và PT.
2. Admin kiểm tra hồ sơ/evidence buổi tập và thông tin người nhận.
3. Admin xác nhận không có tranh chấp mở.
4. Admin duyệt settlement; bước này chưa có nghĩa tiền đã được chuyển.
5. Operator chuyển khoản thật bên ngoài hệ thống.
6. Admin nhập mã giao dịch, tham chiếu chứng từ và thời điểm chuyển tiền.
7. Backend ghi settlement là `paid` và tạo audit.

Không ghi `paid` nếu chưa có giao dịch chuyển tiền thật.

## 11. Hoàn tiền và đối soát

1. Admin hoặc quy trình hỗ trợ tạo yêu cầu refund với số tiền và lý do.
2. Backend kiểm tra tổng refund lũy kế không vượt actual paid.
3. Refund ở trạng thái `Pending` trong khi chờ chuyển tiền/đối soát.
4. Sau khi có chứng từ chuyển tiền hợp lệ, Admin xác nhận refund.
5. Refund chuyển sang `Succeeded`.
6. Sổ cái tạo các bút toán điều chỉnh thay vì sửa hoặc xóa lịch sử cũ.

Webhook lặp, request retry hoặc callback đến sai thứ tự không được tạo thêm doanh thu hay hoàn tiền trùng.

## 12. Trách nhiệm của từng ứng dụng

### Backend

- Kiểm tra authentication, role và ownership.
- Thực thi state machine và quy tắc 24 giờ.
- Xác minh payment webhook.
- Tính toàn bộ số tiền và ghi ledger.
- Chống request trùng, double booking và payout trùng.
- Phát outbox event cho email, push và side effect bất đồng bộ.

### Mobile App

- Hiển thị dữ liệu từ API qua Repository/API adapter.
- Không tự đổi trạng thái booking hoặc payment.
- Không coi redirect thanh toán là bằng chứng đã trả tiền.
- Chỉ hiện nút hủy khi còn ít nhất 24 giờ, nhưng Backend vẫn là lớp kiểm tra cuối cùng.
- Hiển thị rõ trạng thái chờ xác nhận, tranh chấp và hoàn tiền.

### Admin Web

- Hiển thị đúng trạng thái booking và payment riêng biệt.
- Không tự tính tiền bằng dữ liệu giao diện nếu Backend đã trả projection.
- Bắt buộc reason/evidence cho thao tác nhạy cảm.
- Không gọi thao tác settlement hoặc refund như hành động demo.

## 13. Các trường hợp cần kiểm thử xuyên hệ thống

1. Hai Hội viên giữ cùng một slot: chỉ một người thành công.
2. Hold hết hạn rồi mới có webhook thanh toán: không chiếm lại slot.
3. Webhook gửi lặp: payment chỉ được ghi nhận một lần.
4. Hội viên hủy trước 24 giờ: thành công.
5. Hội viên hủy dưới 24 giờ: bị chặn và được hướng dẫn liên hệ hỗ trợ.
6. PT hoàn thành, Hội viên xác nhận: chuyển `Completed` và ghi ledger một lần.
7. PT hoàn thành, Hội viên không phản hồi: tự hoàn thành sau 24 giờ.
8. Hội viên khiếu nại: chuyển `Disputed`, không payout.
9. Admin cố duyệt payout trước 24 giờ sau hoàn thành: bị chặn.
10. Có refund pending hoặc dispute: payout bị chặn.
11. Retry xác nhận/hoàn tiền/chi trả: không nhân đôi tiền hoặc event.
12. PT chưa được duyệt: không thể công bố dịch vụ có thể booking.

## 14. Những việc không được suy đoán

- Không xem Payment `Paid` đồng nghĩa buổi tập đã `Completed`.
- Không xem Booking `Cancelled` đồng nghĩa tiền đã hoàn.
- Không xem Admin approve settlement đồng nghĩa đã chuyển khoản.
- Không xem file chứng chỉ tồn tại đồng nghĩa chứng chỉ là thật.
- Không cho frontend tự quyết định phí, payout hoặc quyền truy cập.
- Không bật payment, AI hoặc push production khi provider chưa được cấu hình và nghiệm thu.

## 15. Tài liệu liên quan

- `docs/backend-release/business-requirements.md`
- `docs/backend-release/domain-rules.md`
- `docs/backend-release/authorization-matrix.md`
- `docs/backend-release/api-contract.md`
- `docs/backend-release/openapi/fitmate-v1.yaml`
- `docs/backend-release/runbooks/payos-manual-settlement.md`
- `docs/backend-release/handoff.md`
