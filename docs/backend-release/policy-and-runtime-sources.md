# Nguồn kiểm tra release
Ngày kiểm tra: 2026-09-22. Đây là reference/gate, không chứng nhận pháp lý hay Play approval.

- Microsoft: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core và https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/ — .NET8 hết hỗ trợ trong tháng11/2026. BE-828 kiểm ngày chính thức lúc release, upgrade trước EOL.
- Account deletion: https://support.google.com/googleplay/android-developer/answer/13327111 — app có tạo tài khoản cần đáp ứng đường dẫn xóa trong app và web theo policy; disclosure retention rõ.
- Health declaration: https://support.google.com/googleplay/android-developer/answer/14738291 — Fitmate cần khai hoạt động fitness/nutrition phù hợp tính năng, không chỉ app chẩn đoán mới khai.
- UGC: https://support.google.com/googleplay/android-developer/answer/9876937 — endpoint fetch không thành công trong lần kiểm tra này; OPS-07 phải mở lại policy và kiểm report/block/moderation trước release.
- Billing FAQ: https://support.google.com/googleplay/android-developer/answer/10281818 — endpoint bị chặn khi fetch lần này; không khẳng định mọi dịch vụ PT online/VIP được miễn Play Billing. OPS-03/07 phân loại SKU physical PT, online1:1 và digital riêng.
- Target API level, account-specific testing/verification, forms/data safety phải kiểm trong Console trước upload; không đóng đinh số target API từ trí nhớ.
