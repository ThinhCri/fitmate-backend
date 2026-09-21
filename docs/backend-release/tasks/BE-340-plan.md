# BE-340 — Secrets management (gitleaks + runbook) — Plan rev 1

**Ngày**: 2026-09-21
**Phiên**: 34 (kế tiếp BE-330 + BE-140)
**Tác giả**: AI (theo user instruction "làm đi")

## Bối cảnh

`tasks.md` BE-340 scope: `.env.example`, `.gitignore`, secret scanning ở CI. Phần 1+2 đã có sẵn ở phiên 1 (`ff95843` foundation). Phần 3 (gitleaks CI) chưa có. Ngoài ra BE-300/200/380 đã reference đến "DataProtection key production path" và "secret manager" nhưng chưa có runbook để operator tham chiếu khi deploy.

DEC-014 (no secrets commit) + DEC-040 (DataProtection key ring path) cần một entry-point duy nhất ghi rõ cách vận hành secrets trong CI + staging + production.

## Mục tiêu

1. **Gitleaks quét repo** — phát hiện secret đã commit + secret mới qua pre-commit/CI.
2. **CI fail-fast** — PR có secret leak → block merge (gộp vào CI workflow BE-310).
3. **Operator runbook** — ghi rõ secret manager workflow + DataProtection key production path + rotation policy.

## Phạm vi (in scope)

### File mới

- **`.gitleaks.toml`** — config gitleaks với custom rules Fitmate:
  - `JWT_SIGNING_KEY` / `Jwt__SigningKey`
  - `DB_PASSWORD` / `POSTGRES_PASSWORD`
  - `VNPAY_HASH_SECRET` / `VNPAY_TMN_CODE`
  - `BOOTSTRAP_ADMIN_EMAIL` (chỉ cảnh báo; demo email `admin@fitmate.vn` OK ở `.env.example` vì đã DEC-014 chốt placeholder)
  - `SMTP_PASSWORD`
  - `STORAGE_LOCAL_PATH` (chỉ cảnh báo nếu là absolute path production)
  - `SIGNING_KEY` (DataProtection) — allowlist `.env.example` chứa placeholder
  - AWS / Google / Stripe key format
  - `extend` default `gitleaks/gitleaks.toml` + custom rules trên.
- **`docs/backend-release/runbooks/secrets.md`** — 6 sections:
  1. **Nguyên tắc** — DEC-014, không commit; chỉ `.env.example`; secret đi qua secret manager.
  2. **Danh mục secrets** — liệt kê 9 secret theo nhóm (DB / JWT / SMTP / VNPay / DataProtection / Storage / Admin bootstrap / Push provider / Monitoring).
  3. **Local dev** — copy `.env.example` → `.env` (gitignored), điền giá trị, không share.
  4. **CI** — ephemeral `JWT_SIGNING_KEY` (BE-310 đã có); gitleaks scan history + filesystem.
  5. **Staging / Production** — secret manager (GitHub Actions secret / DO secret / Vault); rotation policy đề xuất 90 ngày cho JWT/SMTP/VNPay, 365 ngày cho DB password.
  6. **DataProtection key ring (UA-DP-001)** — production path: persistent volume hoặc Azure Key Vault / AWS KMS (post-MVP). Local: default file-based.

### File sửa

- **`.github/workflows/ci.yml`** — thêm job `secrets-scan` chạy song song `lint-test`:
  - Action `gitleaks/gitleaks-action@v2` ở chế độ scan full repo (default).
  - `GITLEAKS_ENABLE_HISTORY=true` để scan cả Git history (phát hiện secret đã commit và xóa sau).
  - `GITLEAKS_ENABLE_UPLOAD_ARTIFACT=true` để upload SARIF report.
  - Fail trên `exit code != 0` (= có leak).
- **`docs/backend-release/tasks.md`** — BE-340 → DONE + entry.
- **`docs/backend-release/handoff.md`** — phiên này.

## Ngoài phạm vi (out of scope)

- Tự tạo tài nguyên secret manager (UA-002/003/004..006 vẫn pending).
- Pre-commit hook ở repo local (chỉ CI gate; user tự cài `pre-commit` nếu muốn).
- Rotate JWT/SMTP/VNPay secret thật (operator action — ghi trong runbook §5).
- Custom rules cho GoogleService-Info.plist / Firebase (Android repo FitmateAI_FE — repo khác, có thể thêm sau nếu user yêu cầu).

## Plan commit

1. `docs(be): add BE-340 plan rev 1 [BE-340]` — file này.
2. `chore(be): add gitleaks config with Fitmate custom rules [BE-340]` — `.gitleaks.toml`.
3. `ci(be): add gitleaks scan job with history + SARIF upload [BE-340]` — `.github/workflows/ci.yml` patch.
4. `docs(be): add secrets runbook with manager + DataProtection [BE-340]` — `docs/backend-release/runbooks/secrets.md`.
5. `docs(be): mark BE-340 DONE in tasks and handoff [BE-340]` — `tasks.md` + `handoff.md`.

## Verify recipe

- `gitleaks detect --source . --config .gitleaks.toml --verbose` (local): exit 0, không có leak trên main.
- `gitleaks detect --source . --config .gitleaks.toml --log-opts="--all"` (history scan): exit 0.
- CI workflow: PR mở → job `secrets-scan` chạy, PASS nếu clean.
- Tạo file test `tests/secrets-leak.txt` chứa `JWT_SIGNING_KEY=abc-secret-test-not-real` → gitleaks phải phát hiện (smoke).
- Đọc lại `docs/backend-release/runbooks/secrets.md` 6 sections đầy đủ.

## Rủi ro

- **False positive** trên `.env.example` (chứa placeholder `__SET_*__`). Allowlist qua `[allowlist]` trong `.gitleaks.toml` với regex `__SET_[A-Z0-9_]+__` không thỏa mãn pattern secret thật.
- **History scan tốn thời gian** trên repo lớn. Nếu runtime quá 5 phút → chuyển sang scheduled job nightly thay vì mỗi PR.
- **`BOOTSTRAP_ADMIN_EMAIL=admin@fitmate.vn`** trong `.env.example` không phải leak (đã public trong DEC-013/014) → allowlist.

## Phụ thuộc

- **BE-310** (CI definition) — DONE phiên 31. Job `secrets-scan` chèn vào workflow hiện có.
- **BE-380** (JwtOptionsLoader) — DONE. Đã giải quyết 1 leak surface (`JwtOptions` không register DI → env key sai mapping có thể dẫn đến fallback).
