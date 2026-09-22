# Release gates — bằng chứng bắt buộc
PROD-2026-09-22-v1. Tất cả gate dưới đây chưa được VERIFIED bởi lần docs này.

| Gate | Tiêu chí | Evidence |
|---|---|---|
| G1 Contract | BR→operation→auth→test→client | OpenAPI SHA256, route report, generated TS, breaking diff |
| G2 Identity/privacy | verify/reset/lock/revoke/MFA/deletion/private media | Real PostgreSQL API + browser/mobile E2E |
| G3 Money | slot race, late IPN, refund concurrency, 10/90, payout | Sandbox IDs sanitized, ledger invariants |
| G4 Features | Core BR acceptance; AI/VIP/reward capability explicit | BE-802..819 evidence, no fake prod |
| G5 CI/Docker | clean DB, migration repeat, DP/media continuity, GH jobs green | Run URL, commit/image digest, scans |
| G6 Load | normal/peak/soak, meaningful traffic, no resource leak | k6 raw report, dataset/environment/metrics |
| G7 UAT | Android/admin cùng contract, offline/errors/auth expiry | E2E report/video/commits, no P0/P1 open |
| G8 Operations | Alerts, restore DB/media/keys, incident ownership | Restore timings, rollback rehearsal |
| G9 Product/policy | terms/retention/media/merchant/Play/deletion URL | Operator approvals + current policy check |
| G10 Release | supported runtime, provider config, protected deploy | Manifest + explicit operator approval |

## Targets ban đầu, không phải kết quả đã đạt
- Availability mục tiêu 99.5%/tháng; một API node vẫn SPOF.
- Normal50 RPS/30 phút; peak100 RPS/10 phút; soak20 RPS/2 giờ.
- Core reads p95<=500ms, writes<=1s, unexpected5xx<1%; đo provider/AI riêng.
- Booking20 callers tranh interval chỉ 1 hold. Không chỉ hit health.
- Restore baseline RPO<=24h/RTO<=4h; verify capability nhà cung cấp và budget.
- Queue bounded, retry/dead-letter/expiry quan sát được.
Nếu fail, ghi bottleneck/capacity limit và sửa, không đổi tiêu chí sau chạy chỉ để PASS.

## Release manifest
Version/date; BE/app/admin commits; OpenAPI SHA256; image digest; migration from/to; environment/capabilities.
Test/CI/report links; provider mode; secret reference names (không value); residual issues/owners.
Operator approver; migration recovery; prior compatible image; smoke/rollback triggers; incident contact.
Đừng tự Down migration destructive như rollback mặc định.
