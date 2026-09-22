# Fitmate — Nguồn chuẩn sản phẩm và production
Baseline **PROD-2026-09-22-v1**, khảo sát 2026-09-22. Kế hoạch hoàn thiện, chưa chứng nhận production-ready.

## Đọc theo thứ tự
0. [Plan triển khai từng task](production-plans/README.md): 30 plan BE, providers/lifecycle và đường dẫn plan hai FE.
1. [Business requirements](business-requirements.md): sản phẩm, phạm vi, luồng nghiệm thu.
2. [Domain rules](domain-rules.md) và [Authorization](authorization-matrix.md): quyền/trạng thái/tiền/timezone.
3. [API conventions](api-contract.md) và [OpenAPI](openapi/fitmate-v1.yaml): spec hiện còn drift cần BE-801 đối chiếu.
4. [Feature matrix](feature-api-matrix.md): BR → app/admin → API/gap → task.
5. [Tasks](tasks.md): backlog BE-800..829 và crosswalk task cũ.
6. [Release gates](release-checklist.md), [operator actions](operator-actions.md), [handoff](handoff.md).

## Thẩm quyền
User mới nhất → baseline này/DEC-051 → domain/authorization → OpenAPI đã đối chiếu → generated DTO → UI adapter.
Spec khác code là gap cần sửa, không có nghĩa endpoint đã hoạt động.
Prototype là inventory nội dung, không là nguồn chuẩn ID/quyền/tiền.

## Ba repo và môi trường
BE: ASP.NET Core Identity + EF/Npgsql/PostgreSQL. App: FitmateAI_FE/mobile React Native/Expo Android Member/PT. Web: FitmateWebApp admin-only.
Cả hai frontend đọc docs/production/ tại repo mình. Đồng bộ baseline version + OpenAPI SHA-256 + generated types khi tích hợp.
Local DB Compose; production baseline managed PostgreSQL Singapore + standby HA theo DEC-030, không phải read replica.
Local media persistent; production durability/backup phải qua BE-816/825. Không tự mua cloud.
.NET8 là stack đang có; vòng support phải qua BE-828 trước release. Identity không mặc định BCrypt riêng như README cũ.
[Local runbook](runbooks/local-dev.md) là hướng dẫn khởi động; migration bundle không cần SDK host.
Không fake payment/AI/push trong production, không tự deploy/push/rewrite.

## Tài liệu lịch sử
[Documentation maintenance](documentation-maintenance.md) ghi nội dung được thay và những file giữ.
tasks/BE-*-plan.md và handoff cũ là lịch sử scope/evidence; trạng thái production đọc tasks.md mới.
