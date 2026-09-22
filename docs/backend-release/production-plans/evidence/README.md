# Evidence template — chưa có task nào PASS chỉ vì có plan
Tạo <TASK-ID>.md khi task thực sự chạy. Không tạo placeholder kết quả xanh.

## Identity
Task/plan revision; baseline; operator/developer; date; BE/app/admin commit; working-tree changes included.
## Scope
In/out delivered; actual files/routes/schema changed; previous/new contract hash.
## Commands và kết quả
Command; environment (sanitized); exit code; passed/failed/skipped; artifact path/CI URL.
Không ghi password/token/connection string hoặc fake số tests.
## Behavior
Happy path; relevant errors; authorization; retries; concurrency/transaction; timezone/money; provider outcome.
## Migration/recovery
Precheck counts; backup reference; migration ID; compatibility; restore/rollback drill hoặc N/A có lý do.
## Client handoff
DTO/examples/error mapping; screen impact; generated types revision; required app/web task.
## Remaining gaps
Issue/owner/dependency/operator action; ảnh hưởng gate; phần đã implement nhưng chưa verify.
## Decision
Status transition justified; reviewer; production approval riêng (nếu có). Không tự đánh RELEASED.
