# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

- **Ngày**: 2026-09-18
- **Phiên**: Khởi tạo docs nền tảng backend release.
- **Commits trong phiên**:
  - BE `ff95843` — `docs(be): foundation — source-of-truth docs, OpenAPI v1, .env.example, Docker Compose skeleton [BE-099-partial]` (32 files, 6542 insertions).
  - FE `bb8207f` — `docs(fe): backend integration pointer + mobile/.env.example [INT-MOB-prep]` (3 files).
  - Web `76766f8` — `docs(web): backend integration pointer + .env.example [INT-WEB-prep]` (3 files).

## Đã làm

- Khảo sát 3 repo, xác minh cấu trúc thực tế.
- Chốt quyết định sản phẩm với user:
  - Stack: .NET 8 + ASP.NET Core + EF Core + Npgsql + Postgres self-hosted.
  - Payment: VNPay sandbox.
  - Deploy: Docker Compose trên VPS.
  - Contract-first cho cả 2 frontend.
- Tạo branch `backend-release/*` ở cả 3 repo.
- Tạo bộ docs nền tảng trong `docs/backend-release/` của BE repo:
  - README, architecture, feature-api-matrix, domain-rules, authorization-matrix, decisions, data-model, migrations, api-contract, openapi/fitmate-v1.yaml, android-integration-guide, admin-integration-guide, ai-contract, ai-handoff-checklist, capacity-plan, load-test-report, deployment-runbook, runbooks/, release-checklist, user-actions, tasks.
- OpenAPI spec hợp lệ: 69 endpoints, 53 schemas, 16 tags.
- Tạo `.env.example`, `.dockerignore`, `.gitignore` chuẩn .NET.
- Tạo `compose.yml` + `compose.override.yml` skeleton.
- Tạo `AGENTS.md` mới cho BE repo.
- Tạo pointer + `.env.example` cho FE repo và Web repo (không đụng working tree của user).
- Working tree của user giữ nguyên ở cả 3 repo.

## Việc cần làm tiếp

### Ngay (sau khi user duyệt docs)

- Commit docs nền tảng vào BE repo branch `backend-release/contract-and-foundation`.
- Tạo `.env.example`, `.dockerignore`, `.gitignore` chuẩn .NET trong BE.
- Viết `AGENTS.md` mới cho BE repo (theo mẫu `docs/backend-release/README.md`).
- Viết CI GitHub Actions cho 3 repo (BE, Web, Mobile).
- Tạo docker-compose.yml + Dockerfile cho BE.

### Sau khi user xác nhận

- Bắt đầu viết code .NET 8 (BE-001 → BE-099 trong `tasks.md`).

## Working tree

- Cả 3 repo: working tree nguyên vẹn (không đụng đến file của user).
- BE repo: đã tạo 18 file mới trong `docs/backend-release/` (chưa commit).
- FE + Web repo: chưa có thay đổi (chỉ tạo branch).

## Tests đã chạy

- Validate YAML cho OpenAPI: PASS.

## Kết quả đo tải

- Chưa (BE chưa có implementation).

## Blockers

| ID | Blocker | Người phụ trách | Cách gỡ |
|---|---|---|---|
| B-001 | UA-001 rotate Supabase password | User | Reset qua Supabase dashboard |
| B-002 | UA-008 VNPay merchant | User | Đăng ký merchant VNPay |
| B-003 | UA-009 Android keystore | User | Tạo qua Android Studio + upload key |
| B-004 | AI handoff | AI team | Dùng `ai-handoff-checklist.md` |
| B-005 | UA-004..UA-006 VPS + domain + TLS | User | Cấp khi đến giai đoạn staging |

## Việc user / nhóm AI

- Xem `user-actions.md` để biết tất cả UA-001..UA-017.
- Bắt đầu từ UA-001 (rotate Supabase) và UA-002 (cấp Git hosting).

## Bước tiếp theo (kế hoạch phiên sau)

1. Commit `docs/backend-release/` vào BE repo.
2. Tạo `.env.example`, `.gitignore`, `.dockerignore` trong BE.
3. Tạo `AGENTS.md` cập nhật cho BE.
4. Tạo `compose.yml` + `compose.override.yml` cho BE local/integration.
5. Viết Dockerfile multi-stage.
6. Tạo GitHub Actions workflow cho BE (lint + test + docker build + smoke).
7. Bắt đầu BE-001 scaffold .NET solution.
