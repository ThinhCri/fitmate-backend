# Archived handoff — review changes overview (2026-09-29)

> ARCHIVED / NON-AUTHORITATIVE. This directory preserves historical review evidence only.
> Do not use its statuses, commit references, routes, or findings as the current contract.
> Current sources of truth are `docs/backend-release/README.md`, `tasks.md`,
> `handoff.md`, `decisions.md`, and `openapi/fitmate-v1.yaml`.

## Phạm vi ghi nhận

Ghi nhận các thay đổi đang dở đã có trong working tree trước khi Antigravity tiếp quản,
phân biệt với phần Antigravity sửa/sửa tiếp. Chỉ ghi những thay đổi có source thực
trong repo, không quy toàn bộ dirty diff là do Antigravity viết.

## HEAD tham chiếu

- Web `76766f8e2f37474ab3467307ba0cf9af71c74d6a`
- BE `12b9bcab53d24aceb70e7934a406c51a0658f21d`

## Cấu trúc review

1. Phần A: code đã có trước khi Antigravity tiếp quản (giữ lại nếu đúng).
2. Phần B: sửa tiếp (chỉ ghi file + behavior thay đổi, không liệt kê full diff).
3. Phần C: code còn gap nhưng không sửa (chờ operator/quyết định hoặc ngoài scope).

Cập nhật lịch sử từng đợt nằm trong `changes.md`; nội dung này không còn là hướng dẫn thực thi hiện tại.

## Đợt hiện tại

- **F1 (auth/session/MFA)** — đã chạm `src/api/client.ts` (ghi chú Authorization-preserve), `tests/Fitmate.UnitTests/AuthSessionServiceTests.cs` (NEW, invariant).
- **F2 (finance)** — đã refactor `src/features/finance/ManualFinancePanel.tsx` thành 5-phase state machine; thêm `src/features/finance/__tests__/ManualFinancePanel.test.tsx`; polyfill `HTMLDialogElement.showModal` ở `src/test/setup.ts`.
- **F3 (certificates atomic)** — `IAuditLogger.Stage` + EfAuditLogger `Build` helper; `CertificateService.Revoke/Approve/Reject` đổi sang cùng-transaction; `Revoke` thêm validate lý do.
- **F5 (overview/settings)** — `overviewApi.DeadLetterItem` dùng `createdAt` thật; `Overview.tsx` & MSW fixture đồng bộ.
- **F4 (booking review)** — `BookingReview.tsx` (NEW) 3 nhóm; wired vào `BookingManagement.tsx`.
- **F3b (cert pagination + ownership)** — `ICertificateStore.ListByPtPagedAsync` (BE); `Certificate.ImageUploadedByUserId` + migration `20260928130000_CertificateOwnership`; `AdminCertificatesController.Preview` enforce 404/403; `UploadsController` nhúng userId vào storage key; `CertificatesController.ImageKeyBelongsTo` chặn key của người khác; test `CertificateServiceTests` (+5 case), `AdminCertificatesControllerOwnershipTests` (4 case), `CertificateImageKeyOwnershipTests` (9 case).
- **F1c (idempotency scope)** — `IdempotencyMiddleware.ShouldStore` thu hẹp còn 2xx + 409 (400/422 không cache để retry được với payload đã sửa); test `IdempotencyMiddlewareTests` 3 case mới.
- **F6b (contract + tài liệu)** — `openapi/fitmate-v1.yaml` thêm 6 path + response `202`; `authorization-matrix.md` thêm mục cert image preview ownership; `FinanceStepUpTests` (NEW, 11 case) — handoff cũ ghi sai là finance step-up chưa wire.
- **F7 (password policy — user chốt 2026-09-28)** — `Fitmate.Domain/Common/PasswordPolicy.cs` (NEW) `MinimumLength = 12`; thay `MinimumLength(8)` ở Register/Reset/Change validators, `IdentityOptions.Password.RequiredLength`, 3 CLI seed, OpenAPI `minLength: 8 → 12`. `PasswordPolicyTests` (NEW, 10 case).
- **F8 (R08 — lỗi 500 thay vì 400/409, rà mở rộng)** — Đọc kỹ cho thấy handoff cũ chỉ nêu 1 chỗ, thực tế là pattern lặp. Đã sửa:
  1. `SupportService` — 4 chỗ `ArgumentException` → `ValidationException` có errors map; reply ticket `Closed` → `ConflictException` 409 (thêm `ErrorCodes.Support.TicketClosed`); 2 list filter thêm `Enum.IsDefined` để `status=99` không lọc nhầm thành 0 dòng.
  2. `AdminFinanceService.ParseType` — `InvalidOperationException` khi gặp `ledger_entries.type` lạ làm hỏng cả trang ledger; đổi sang `LedgerEntryType?` + lọc dòng.
  - Test: `SupportServiceTests` (NEW, 20 case).
  - Giữ nguyên có chủ đích: `Money`, `PasswordHasher`, `OutboxDispatcher`, `IdempotencyServiceImplementation`, `PushPayload` (internal invariant / outbox worker, không phải input HTTP).
- **F9 (build fix — chặn CI, không phải gap nghiệp vụ)** — Trước đợt này solution **không build được**, không chỉ do code mới mà cả do code đã có sẵn trong working tree:
  1. `Program.cs` — `using Fitmate.Api.Errors` + `using Fitmate.Domain.Common` cùng khai báo `ErrorCodes` ⇒ 6× `CS0104`. Đã đổi các call-site thành tên đầy đủ `Fitmate.Api.Errors.ErrorCodes.*` (bản wire-level; bản domain vẫn qua `using`).
  2. `PasswordPolicyTests` — `[InlineData("ElevenChars"), false]` thiếu ngoặc (`CS1041`), và `[InlineData(null, false)]` không hợp lệ cho tham số `string` attribute ⇒ tách case null thành `[Fact]`.
  3. Stub test thiếu method của interface mới: `IAuditLogger.Stage` (3 file), `ICertificateStore.ListByPtPagedAsync` (2 file), `ISignedUrlService.TryValidateAndExtractPath` (1 file).
  4. `CertificatesController.ImageKeyBelongsTo` là `internal static` ⇒ thêm `<InternalsVisibleTo Include="Fitmate.UnitTests" />` vào `Fitmate.Api.csproj` thay vì mở API public.
  5. `FinanceStepUpTests` viết theo API Identity .NET 8 trong khi repo đang trên **.NET 10** (target framework của cả solution). Sai 6 chỗ: `IUserLockoutStore`/`IUserEmailStore` thiếu type argument; `UpdateAccessFailedCountAsync` đã đổi thành `IncrementAccessFailedCountAsync`; `TokenOptions.AuthenticatorTokenProvider` là **string tên provider** (đăng ký qua `Tokens.ProviderMap[name] = new TokenProviderDescriptor(type)`), không phải object; `NullPasswordHasher` đã bị loại khỏi Identity ⇒ dùng `PasswordHasher<T>`; `KeyNormalizer` là `UpperInvariantLookupNormalizer`; `UserManager<T>` ctor cần `IServiceProvider` và cần store implement `IUserTwoFactorStore<T>`.
  6. `AuthValidatorTests.RegisterRequest_Valid_Member` dùng mật khẩu 11 ký tự, dưới ngưỡng 12 của `PasswordPolicy` (F7) ⇒ đổi thành 12 ký tự.
  - **Kết quả**: `dotnet build` 0 error, `dotnet test` 700/700 pass (2026-09-29).
  - **Ghi chú cho về sau**: `AGENTS.md` mô tả stack là .NET 8 nhưng `TargetFramework` thực tế là `net10.0`. Test viết theo API .NET 8 sẽ không compile. Nên xác nhận target framework rồi cập nhật `AGENTS.md`.

Trạng thái các thay đổi: **BE unit test ĐÃ xác minh 2026-09-29** — `dotnet build tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj` 0 error, `dotnet test` **700/700 pass**. Phần còn lại do user chạy: `npm run typecheck`/`test:run`/`build` ở Web, integration test BE (cần Postgres), apply migration, OpenAPI lint.

## Migration chưa apply

`20260928130000_CertificateOwnership` mới được viết, chưa apply vào DB nào. Pre-flight, thứ tự thao tác và lệnh rollback nằm ở `docs/backend-release/handoff.md` § 2026-09-28. KHÔNG chạy apply lên DB có dữ liệu thật khi chưa có review.

## Lệnh kiểm chứng (user thực thi)

```powershell
# Web
Set-Location D:\Ky8\exe201\FitmateWebApp
npm ci
npm run typecheck
npm run test:run
npm run build

# Backend
Set-Location D:\Ky8\exe201\FitmateAI_BE
dotnet build src/Fitmate.Api/Fitmate.Api.csproj
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj
# Integration tests cần Postgres theo runbook docs/backend-release/
```

Chạy riêng test mới của BE:

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_BE
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj `
  --filter "FullyQualifiedName~Certificate|FullyQualifiedName~IdempotencyMiddleware|FullyQualifiedName~FinanceStepUp|FullyQualifiedName~AuthSession"
```

## Quyết định đã chốt

- **SET-W01 (2026-09-28)**: mật khẩu tối thiểu **12 ký tự** ở mọi nơi. BE dùng `PasswordPolicy.MinimumLength`; OpenAPI `minLength: 12`; Web `Settings.tsx` đã là 12. Không tác động mật khẩu đã tồn tại.

## Lỗ hổng phát hiện khi rà soát (2026-09-28)

- **R19 — storage key không chứa user id**: `UploadsController` sinh key `{category}/{safeName}_{guid}{ext}`,
  nên một PT có thể submit storage key của tài khoản khác và làm check "uploader == owner" ở preview
  vô hiệu hoàn toàn. Đã sửa: key nay nhúng `userIdN`, và `CertificatesController.Submit` kiểm scope key.
  Chi tiết: `risks-and-verification.md` §R19.
