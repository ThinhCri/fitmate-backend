# Dọn tài liệu và bảo toàn thay đổi
2026-09-22; docs-only, không xóa runtime code/migrations/data.
## Nội dung thay thế
README lỗi thời, tasks BE-099 trùng/BE-130 DONE+TODO/tổng sai, feature matrix Spring Boot cũ được viết lại.
Domain/API/authz sửa UUID, state diagram, private cert, recognition, marker timestamp-only và mock assumptions.
Capacity-plan bỏ giá/provider quota và số liệu “nội bộ” chưa được chứng minh, chuyển targets kiểm thử.
## Giữ lại
decisions/handoff và tasks/BE-*-plan.md giữ history/evidence; thêm supersession notices.
audit-old-backend/identity-evaluation/managed-postgres-comparison giữ lịch sử; không dùng làm production config hiện hành.
Runbooks/migrations không thừa chỉ vì chưa dùng.
FE legacy screens/node_modules tracked cần WEB-807 audit trước xóa.
## Không chạm
Code user chưa commit (notifications/social/moderation/payment), user-actions.md và plans chưa tracked.
UI/mock fixtures còn là inventory parity; thay bằng adapters ở task FE, không âm thầm bỏ chức năng.
Không xóa volumes/.env, không rewrite history/deploy/auto-commit.
## Cleanup tiếp theo
Trước xóa: references + routes/build/tests, chuyển unique requirements, cập nhật backlinks, ghi path/reason.
Không nhân bản docs cũ chứa secret vào archive mới. Phiên bản trước tracked có trong Git.
