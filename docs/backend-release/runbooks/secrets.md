# Runbook — Secrets Management

> Quy trình vận hành secrets qua các môi trường (local / CI / staging / production) cho Fitmate Backend. Căn cứ: DEC-014 (không commit secrets), DEC-040 (DataProtection key ring), UA-DP-001 (key rotation policy), BE-340 (gitleaks CI gate).

---

## 1. Nguyên tắc

1. **Không commit secret thật vào Git.** Chỉ `.env.example` (placeholder) và `.gitleaks.toml` (regex detection).
2. **Mọi secret trong production đi qua secret manager** (GitHub Actions secret cho staging, DigitalOcean secret / Vault / SOPS cho production).
3. **Local dev** copy `.env.example` → `.env` (gitignored), điền giá trị bằng tay. Không share file `.env` qua chat / email / commit.
4. **Rotation**: secret có TTL đề xuất; việc rotate thuộc trách nhiệm operator (UA-DP-001 §5).
5. **CI ephemeral**: workflow CI tạo JWT_SIGNING_KEY runtime qua `openssl rand` — không cần GitHub secret cho job này (BE-310 đã chốt).

## 2. Danh mục secrets

| Tên biến | Nhóm | Default placeholder | Mô tả |
|----------|------|---------------------|--------|
| `DB_PASSWORD` | Database | `__SET_LOCAL_PASSWORD__` | Postgres password (local self-host / managed). |
| `JWT_SIGNING_KEY` | Auth | `__SET_64_BYTES_BASE64__` | HS256/HS512 signing key (≥32 bytes; production ≥64 bytes). |
| `JWT_ISSUER` / `JWT_AUDIENCE` | Auth | `fitmate-api` / `fitmate-clients` | JWT claim `iss`/`aud`. Có thể là config thường, không nhạy cảm. |
| `BOOTSTRAP_ADMIN_EMAIL` | Auth | `admin@fitmate.vn` | Email admin đầu tiên (chỉ dùng cho CLI `seed-admin`). |
| `SMTP_PASSWORD` | Email | `__SET_SMTP_PASSWORD__` | SMTP app password (SendGrid/Mailgun/SES). |
| `VNPAY_TMN_CODE` | Payment | `__SET_VNPAY_TMN__` | VNPay merchant TMN. |
| `VNPAY_HASH_SECRET` | Payment | `__SET_VNPAY_HASH_SECRET__` | VNPay HMAC SHA-512 secret. |
| `STORAGE_*` | Storage | `__SET_*__` | S3-compatible access key + secret (backblaze/DO Spaces/MinIO). |
| `AI_PROVIDER` | AI | `stub` | Provider id; production wire (BE-430) cần API key riêng. |
| `PUSH_PROVIDER_*` | Push | — | Expo Push Token hoặc FCM service account JSON (BE-200/UA-FCM-001). |

## 3. Local dev

```bash
cp .env.example .env
# Sửa .env: điền giá trị thật cho DB_PASSWORD, JWT_SIGNING_KEY, ...
# (Đã có trong .gitignore — verify bằng `git check-ignore .env`)
docker compose up postgres
dotnet ef database update --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
dotnet run --project src/Fitmate.Api
```

**Checklist trước khi commit:**
- `git status` không có `.env` hoặc bất kỳ file ngoài `.env.example`.
- `git diff --staged` không chứa chuỗi `__SET_*__` (placeholder OK vì là regex allowlist của gitleaks).
- Chạy `docker run --rm -v "$PWD:/repo" -w /repo zricethezav/gitleaks:latest detect --source . --config .gitleaks.toml` → exit 0.

## 4. CI (GitHub Actions)

CI workflow `.github/workflows/ci.yml` có 4 job song song:
- `secrets-scan` — gitleaks quét working tree + full Git history. Fail trên leak.
- `lint-test` — `dotnet build` (warnings-as-errors, trừ `xUnit1031`) + `dotnet test` + Spectral OpenAPI lint.
- `docker-build` — Buildx cache, build `api` + `migrate` images.
- `smoke` — compose up postgres+migrate+api, poll `/health/ready` 90s, teardown.

**Ephemeral secrets:**
- `JWT_SIGNING_KEY` — `openssl rand -base64 64` runtime; ghi vào `.env` ephemeral trước `docker compose up`. Workflow xóa `.env` cuối job.
- `DB_PASSWORD=ci-ephemeral-db-password` — fix cứng cho compose ephemeral stack. Không dùng cho production.

**Production-like secrets chưa cần ở CI** (chỉ runtime smoke): DB thật, SMTP, VNPay sandbox. Khi wire staging E2E (BE-500) → set GitHub Actions secret `STAGING_DB_URL`, `STAGING_JWT_SIGNING_KEY`, ...

## 5. Staging / Production

### 5.1 Secret manager

| Môi trường | Backend secret manager | Phương thức inject |
|------------|------------------------|---------------------|
| Staging | GitHub Actions secret + Docker `--env-file` (chỉ cho ephemeral stack) | Workflow ghi `.env` từ `${{ secrets.* }}`. |
| Production baseline | DigitalOcean App Platform env var HOẶC Docker secret (compose v2 `secrets:` block) | Inject runtime; KHÔNG bake vào image. |

### 5.2 Rotation policy (đề xuất)

| Secret | TTL rotation | Lý do |
|--------|--------------|--------|
| `DB_PASSWORD` | 365 ngày | DB connection string phức tạp — rotate kỳ thay vì incident. |
| `JWT_SIGNING_KEY` | 180 ngày | Refresh token TTL = 30 ngày; rotation invalidate mọi session cũ. |
| `SMTP_PASSWORD` | 90 ngày | Theo khuyến nghị SendGrid/Mailgun. |
| `VNPAY_HASH_SECRET` | 365 ngày | VNPay sandbox/prod tách riêng; sandbox có thể rotate thường xuyên hơn. |
| `STORAGE_*` keys | 180 ngày | S3-compatible access key rotation. |
| `PUSH_PROVIDER` | 365 ngày | FCM service account JSON rotate khi Google khuyến nghị. |

**Rotation procedure:**
1. Generate secret mới qua secret manager.
2. Stage update: deploy bản mới song song với bản cũ (dual-write hoặc fallback window).
3. Verify health check OK → disable secret cũ trong secret manager.
4. Sau TTL rotation window → xóa secret cũ.

### 5.3 Audit & alert

- CI gitleaks report upload SARIF artifact (`gitleaks-sarif-{run-id}`).
- Production logging: Serilog JSON có `traceId`, `userId`, `path`, `latencyMs` — KHÔNG log secret value. DEC-019.
- Monitoring alert (UA-016): `/health/ready` fail trong >5 phút → page operator.

## 6. DataProtection key ring (UA-DP-001)

### 6.1 Local / Dev

- Default file-based key ring dưới `~/.aspnet/DataProtection-Keys/` (Windows) hoặc `/home/fitmate/.aspnet/DataProtection-Keys/` (Docker).
- Auto-rotate qua `DataProtectionOptions.KeyExpirationPolicy`.
- Compose: named volume `fitmate-dpkeys` mount vào path này để persist qua container restart.

### 6.2 Staging

- Bind volume `fitmate-dpkeys:/home/fitmate/.aspnet/DataProtection-Keys`.
- Backup volume nightly cùng DB backup.

### 6.3 Production (post-MVP)

- **`PersistKeysToAzureKeyVault`** / **`PersistKeysToAwsKms`** — secret manager cho key ring (chưa chốt provider, phụ thuộc UA-002/003).
- **Rotation**: key tự động rotate theo KeyExpirationPolicy; ciphertext cũ vẫn decrypt được nhờ key id embedded.
- **Disaster recovery**: backup key ring qua secret manager; không bao giờ bake vào image.

### 6.4 Migration từ local key → managed key

1. Cấu hình `PersistKeysToFileSystem` (đã có ở local).
2. Khi sẵn sàng managed provider → cấu hình `PersistKeysTo<KeyVault|Kms>` SAU `PersistKeysToFileSystem` (chained provider).
3. Key mới được tạo ở managed provider; key cũ vẫn decrypt được.
4. Sau rotation window (>1 năm) → xóa file system provider, key ring hoàn toàn ở managed.

## 7. Khi phát hiện secret đã commit

### 7.1 Ngay lập tức

1. **Rotate secret** ở secret manager / dashboard.
2. **Khôi phục version pre-commit** trên remote (UA cần cấp quyền force-push + filter-repo).
3. **Audit log** — kiểm tra access log secret đó có bị truy cập bất thường không (UA-016 monitoring).

### 7.2 Trong repo local

- **`git filter-repo`** (UA-001 cho BE-099 — Supabase password đã commit):
  ```bash
  # Backup trước
  cp -r .git .git-backup-$(date +%s)
  # Xóa file chứa secret khỏi tất cả commits
  git filter-repo --path src/main/resources/application.properties --invert-paths
  # Force-push sau user approval (AGENTS.md: KHÔNG push không được duyệt)
  git push origin --force --all
  ```
- Collaborator phải `git fetch origin` + `git reset --hard origin/<branch>`.

## 8. Files liên quan

- **`.env.example`** — placeholder env vars (committed).
- **`.gitignore`** — `.env*`, `*.pfx`, `*.keystore`, `secrets.json`, `appsettings.*.json` đã loại trừ.
- **`.gitleaks.toml`** — custom rules Fitmate + allowlist placeholder/demo values.
- **`.github/workflows/ci.yml`** — job `secrets-scan` chạy mỗi PR.
- **`docs/backend-release/decisions.md`** — DEC-014 (no secrets commit), DEC-040 (DataProtection key).
- **`docs/backend-release/user-actions.md`** — UA-DP-001 (key ring production path), UA-001 (rotate Supabase password).
- **`docs/backend-release/runbooks/local-dev.md`** — quickstart local dev (BE-300 đã viết).

## 9. Lỗi thường gặp

| Lỗi | Nguyên nhân | Cách xử lý |
|------|------------|------------|
| `JwtOptions.SigningKey must be configured` | `JWT_SIGNING_KEY` rỗng hoặc key <32 bytes | `openssl rand -base64 64` → set env var. Xem BE-380. |
| `Unable to resolve service for type 'JwtOptions'` | `JwtOptions` chưa register DI | Đã fix ở BE-380 (JwtOptionsLoader + DI). |
| Gitleaks false positive trên `dbPassword = builder.Configuration[` | Regex match biến reference | Verify `.gitleaks.toml` regex đã require quoted value (rev 1 fix). |
| DataProtection "key ring was not found" sau restart container | Ephemeral container, không persist volume | Bind named volume `fitmate-dpkeys` (BE-300). |
| SMTP 535 auth failed | SMTP password sai hoặc app password chưa enable | Verify dashboard; cho Mailgun/SendGrid cần tạo "App Password" riêng. |
| VNPay "Invalid signature" | `VNPAY_HASH_SECRET` không khớp TMN | Verify sandbox dashboard cùng cặp; secret case-sensitive. |
