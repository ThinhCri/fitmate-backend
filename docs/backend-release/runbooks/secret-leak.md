# Runbook — Secret leak

> Khi phát hiện secret bị commit, push, hoặc lộ.

## Triệu chứng

- Secret scanner CI báo.
- Thấy credentials trong log / artifact.
- Đối tác / bug bounty báo.

## Bước xử lý NGAY

1. **Đánh giá mức độ**:
   - Secret loại gì (DB, JWT, payment, signing key)?
   - Scope (production / staging / local)?
   - Có trong public Git không?
2. **Revoke / rotate**:
   - DB password: rotate ngay qua provider console.
   - JWT signing key: rotate + invalidate tất cả token (re-login all).
   - Payment secret: rotate qua provider portal.
   - Webhook secret: rotate + update config.
   - SMTP / API keys: rotate.
3. **Audit access**:
   - Xem log access DB / API trong khoảng thời gian secret có thể bị lộ.
   - Phát hiện access lạ → điều tra.
4. **Đóng kênh lộ**:
   - Nếu đã vào commit public: xem bước dưới.
   - Nếu chỉ trong working tree: thay bằng placeholder, gitignore đúng.
5. **Thông báo**:
   - Users nếu data bị ảnh hưởng.
   - Provider nếu cần.

## Nếu secret đã trong public Git history

1. **Coi như compromised**.
2. Rotate ngay.
3. KHÔNG tự ý `git filter-repo` / force-push khi chưa được phép.
4. Nếu được phép rewrite history: backup repo trước, dùng `git filter-repo --invert-paths --path <file>` hoặc `BFG`, sau đó force-push, yêu cầu mọi contributor re-clone.
5. Sau force-push, audit các nơi secret có thể cached:
   - CI cache.
   - Docker layer cache.
   - Deploy artifact.

## Sau sự cố

- Viết postmortem.
- Cập nhật `.gitignore`, `.dockerignore`, secret scanning.
- Cấu hình pre-commit hook.

## Phòng ngừa

- Secret scanning ở CI (gitleaks / trufflehog).
- Pre-commit hook (husky + gitleaks).
- Không echo env trong log.
- Build secret mounts thay vì ARG/ENV.
- OIDC cho CI thay vì long-lived credentials.
