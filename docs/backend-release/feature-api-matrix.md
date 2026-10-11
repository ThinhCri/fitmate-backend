# Traceability — BR, client, API và task
PROD-2026-09-22-v1. C=code/controller thấy tồn tại; S=spec; P=proposed/gap. Chưa gán VERIFIED trong task docs.
BE-801 xuất inventory từng operation/schema. Spec không chứng minh implementation.

| BR | App | Admin | Code/spec/gap | Task |
|---|---|---|---|---|
| 01 | Login/register/settings | Login/MFA/settings | C auth 4 routes; S password; P verify/MFA/cookie | BE-802/803 |
| 02 | Profile/edit/chuyen-sau/search | Users/profile | S users/me; P extended/public UUID/search | BE-804 |
| 03 | Delete/export/settings | Public deletion + cases | P workflows/retention | BE-805 |
| 04 | Marketplace/cert/services/reviews | Trainers | C trainers/certs/services/availability; P reviews/full projection | BE-806 |
| 05 | Booking/appointments | BookingManagement | C hold/cancel/reschedule/complete/list; S confirm/admin list; P accept/start | BE-807 |
| 05 PT packages | Member/PT package list and stop | Admin package read-only review | C participant and Admin package reads, stop/preview; public package checkout still disabled | BE-807/818 |
| 06 | Checkout/payment result | Payments/finance | C stub; S refund/admin payments; P verified provider/reconcile | BE-808/809/400 |
| 07 | Revenue/index/detail | FinanceManagement | S me/revenue; C admin finance; P settlement | BE-810 |
| 08 | Workout/player/history/weight | No raw health | C sessions/weight; S plans; P CRUD/snapshot verification | BE-811 |
| 09 | Nutrition/scan/schedule/allergy | No raw meals | C manual/schedule, scan unavailable; P delete/confirm | BE-812 |
| 10 | Dashboard/streak/macros | Overview khác owner stats | P owner aggregate; C partial admin metrics | BE-813 |
| 11 | Social/friends/search/chat | Reports | C modules; P ordering/race/privacy gaps | BE-814 |
| 12 | Inbox/device lifecycle | Warning/action-required | C inbox/devices; push provider missing | BE-815 |
| 13 | AI Companion/adaptive/scan | Operational AI only if enabled | S AI routes; unavailable nutrition adapter | BE-817 |
| 14 | Không admin trên app | 7 modules | C subsets; S admin audit/bookings/payments/overview | BE-818 |
| 15 | Support/settings/drawer | Settings/support | P ticket/capabilities/preferences/system safe | BE-819 |
| 16 | Media/offline/errors | Auth/errors/audit | C upload mới/health; evidence còn thiếu | BE-816/820..828 |
| 17 | AI usage + Google Play AI Plus | Legacy premium inactive | C `/me/ai-usage`, `/me/ai-subscription`, token verification/binding, encrypted entitlement and reconciliation; native checkout implemented in Mobile but Play UAT/RTDN external gate remains; legacy VIP/rewards disabled | BE-829 |

## Inventory không bị xóa
Mobile FP-001..040 vẫn ở repo app (FP-034 đang trùng nhãn trong tài liệu cũ); dùng BR IDs cross-repo, không renumber lịch sử âm thầm.
Safe-area/tabs/bottom-sheet/timer/audio/checkpoint/image-picker là FE, không cần endpoint riêng cho mỗi UI primitive.
Web legacy Mentor/Student/Premium files: WEB-807 kiểm imports/routes trước cleanup, không xóa chỉ vì tên.
Bảng cũ từ Spring Boot có endpoint giả định và số “chưa có” lỗi thời được thay; truy lịch sử qua Git/handoff.


## Payment/settlement override — 2026-09-26
User chọn tích hợp payOS cho checkout mới và chi PT thủ công sau gọi xác nhận khách + PT + hồ sơ buổi tập. Xem [runbook](runbooks/payos-manual-settlement.md) và handoff mới. Code/migration/tests đã soạn, chưa chạy; evidence cũ không verify thay đổi mới. OPS-03 cần xác nhận provider cho mô hình thu tiền/chi PT trước live. Không tự deploy/commit/chuyển tiền.
