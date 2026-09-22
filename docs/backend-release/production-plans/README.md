# Plan triển khai production — mục lục
Baseline PROD-2026-09-22-v1. Plan rev1; chưa task nào trở thành DONE chỉ vì plan tồn tại.

## Bắt đầu
Đọc [execution guide](execution-guide.md) và [evidence template](evidence/README.md).
Trạng thái implementation nằm ở [tasks](../tasks.md); plan là scope/sequence/tests/recovery, không thay state tracker.
Tên file/schema cuối cùng phải xác minh lúc bắt đầu task; đây không phải lý do dừng mọi việc chờ user.

## Thứ tự thực hiện
1. BE-800 audit baseline → BE-801 freeze contract theo module.
2. BE-820 harness và BE-828 runtime sớm; BE-821 CI foundation làm khi có harness, không đợi toàn bộ features.
3. Identity/profile/privacy → PT/booking/payment/revenue; workout/nutrition/dashboard và social/push theo dependency.
4. Admin/support/storage; providers sandbox và client integration theo endpoint đã VERIFIED.
5. Security/load/observability/restore → BE-826 UAT → BE-827 operator-approved release.

Dependency có milestone, không bắt toàn bộ BE-801/820 hoàn tất cho mọi tính năng trước khi bắt đầu code: xem execution-guide.
Các mặc định sản phẩm mới đã có trong domain-rules; operator duyệt terms/retention/merchant trước live, không developer tự claim policy compliant.

## 30 task production
| Task | Plan |
|---|---|
| BE-800 | [Đối chiếu working tree và dọn baseline](BE-800.md) |
| BE-801 | [Đóng băng contract liên repo](BE-801.md) |
| BE-802 | [Hoàn thiện authentication tài khoản](BE-802.md) |
| BE-803 | [Bảo vệ đăng nhập admin](BE-803.md) |
| BE-804 | [Hồ sơ, privacy và user search](BE-804.md) |
| BE-805 | [Xóa tài khoản và dữ liệu](BE-805.md) |
| BE-806 | [PT onboarding, marketplace và reviews](BE-806.md) |
| BE-807 | [Hoàn thiện lifecycle booking](BE-807.md) |
| BE-808 | [Loại payment giả khỏi production](BE-808.md) |
| BE-809 | [Refund, dispute và reconciliation](BE-809.md) |
| BE-810 | [Doanh thu PT và settlement](BE-810.md) |
| BE-811 | [Workout plan, session và weight hoàn chỉnh](BE-811.md) |
| BE-812 | [Dinh dưỡng và nhật ký đầy đủ](BE-812.md) |
| BE-813 | [Dashboard và tiến độ thật](BE-813.md) |
| BE-814 | [Kiểm chứng social và chat production](BE-814.md) |
| BE-815 | [Thông báo và push thật](BE-815.md) |
| BE-816 | [Storage access và durability](BE-816.md) |
| BE-817 | [Tích hợp artifact AI thật](BE-817.md) |
| BE-818 | [Hoàn thiện 7 module admin](BE-818.md) |
| BE-819 | [Support, settings và capabilities](BE-819.md) |
| BE-820 | [Integration và contract verification](BE-820.md) |
| BE-821 | [Docker và CI/CD bằng chứng thật](BE-821.md) |
| BE-822 | [Security và quản lý secrets](BE-822.md) |
| BE-823 | [Performance và capacity đo thật](BE-823.md) |
| BE-824 | [Monitoring và xử lý sự cố](BE-824.md) |
| BE-825 | [Backup, retention và disaster recovery](BE-825.md) |
| BE-826 | [UAT ba repo và staging](BE-826.md) |
| BE-827 | [Chốt production và Play release](BE-827.md) |
| BE-828 | [Runtime còn được hỗ trợ](BE-828.md) |
| BE-829 | [Bảo toàn backlog VIP và rewards](BE-829.md) |

## Providers và lifecycle legacy
| Task | Cách thực hiện |
|---|---|
| BE-099 | [Rotation và history cleanup](BE-099.md), approval rewrite riêng |
| BE-400 | [VNPay](BE-400.md), adapter + sandbox/live gates |
| BE-410 | [Email](BE-410.md), local/outbox trước delivery credentials |
| BE-430 | [AI handoff](BE-430.md), alias của BE-817, không duplicate implementation |
| BE-500 | [Staging](BE-500.md), orchestration BE-821/826 |
| BE-600 | [Production](BE-600.md), approval package BE-827 |
| BE-700 | [Play](BE-700.md), phối hợp BE-827/MOB-808 |

## Hai frontend
- [Android task plans](../../../../FitmateAI_FE/docs/production/plans/README.md): MOB-800..808.
- [Admin task plans](../../../../FitmateWebApp/docs/production/plans/README.md): WEB-800..807.
Các đường dẫn liên repo dùng checkout sibling; khi repo ở máy khác đọc docs/production tại repo tương ứng.
Giữ contract hash/revision khi chuyển task, không copy secret hoặc ép BE theo mock shape.
