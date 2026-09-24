# Chính Sách Hỗ Trợ Runtime và Lộ Trình Nâng Cấp .NET 10 LTS

Tài liệu này xác lập chính sách quản trị vòng đời runtime, ma trận tương thích toolchain và kế hoạch nâng cấp công nghệ cho toàn bộ hệ thống backend Fitmate theo yêu cầu của **BR-16** và **BE-828**.

---

## 1. Phân Tích Vòng Đời Runtime (Microsoft .NET Lifecycle)

Theo chính sách hỗ trợ chính thức của Microsoft ([Microsoft .NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy)):

| Phiên bản .NET | Loại phát hành | Ngày phát hành chính thức | Ngày hết hạn hỗ trợ (End of Life - EOL) | Trạng thái đối với Fitmate |
| :--- | :--- | :--- | :--- | :--- |
| **.NET 8** | **LTS** (Long Term Support) | 14/11/2023 | **10/11/2026** | **Runtime hiện tại**. Còn khoảng **48 ngày** trước khi hết hỗ trợ. |
| **.NET 9** | **STS** (Standard Term Support) | 12/11/2024 | **10/11/2026** | **Không sử dụng**. Ngày EOL trùng hoàn toàn với .NET 8. |
| **.NET 10** | **LTS** (Long Term Support) | **11/11/2025** | **14/11/2028** | **Mục tiêu nâng cấp của dự án**. Đã phát hành chính thức, hỗ trợ dài hạn đến 2028. |

> [!IMPORTANT]
> **Quy định của BR-16**:
> 1. Không vận hành hệ thống sản phẩm trên runtime đã hết hạn hỗ trợ (EOL).
> 2. Giai đoạn hiện tại tạm thời giữ `.NET 8 LTS` cho đợt triển khai ban đầu, nhưng **bắt buộc phải có kế hoạch nâng cấp lên .NET 10 LTS được hoàn tất trước ngày 10/11/2026**.
> 3. Không dùng .NET 9 làm bước đệm trung gian.

---

## 2. Ma Trận Tương Thích Toolchain & Dependencies

Dưới đây là ma trận khảo sát hiện trạng và các dải phiên bản **ứng viên cần kiểm chứng** (Candidate ranges requiring empirical validation).

> [!NOTE]
> Các dải phiên bản dưới đây là ứng viên kỹ thuật đề xuất, không mặc nhiên coi là tương thích an toàn cho đến khi chạy qua toàn bộ test suite trên branch nâng cấp. Động cơ cơ sở dữ liệu giữ nguyên **PostgreSQL 16**, không mở rộng scope sang PostgreSQL 17 trong task này.

| Thành phần | Phiên bản đang dùng trong repo | Dải ứng viên đề xuất (.NET 8 maintenance) | Dải ứng viên mục tiêu (.NET 10 LTS) | Ghi chú kỹ thuật |
| :--- | :--- | :--- | :--- | :--- |
| **Target Framework Moniker (TFM)** | `net10.0` | N/A | `net10.0` (Active) | Toàn bộ 6 project đã chuyển sang `net10.0`. |
| **.NET SDK Host** | `global.json` (10.0.100) | N/A | `10.0.1xx` (Active) | Đã cấu hình `global.json` với `rollForward: "latestFeature"`. |
| **ASP.NET Core Runtime** | `8.0.10` | `8.0.x` latest security patch | `10.0.x` | Theo dõi bản vá bảo mật hàng tháng của Microsoft. |
| **dotnet-ef CLI Tool** | Global 8.0.10 (Dockerfile) | `8.0.x` | `10.0.x` | Cần bổ sung tool manifest `.config/dotnet-tools.json`. |
| **Microsoft.EntityFrameworkCore** | `8.0.10` | `8.0.x` (>= 8.0.10) | `10.0.x` | Kiểm tra translation query và shadow state. |
| **Npgsql.EntityFrameworkCore.PostgreSQL** | `8.0.10` | `8.0.x` (>= 8.0.10) | `10.0.x` | Tương thích PostgreSQL 16 engine hiện tại. |
| **Identity & JwtBearer** | `8.0.10` | `8.0.x` (>= 8.0.10) | `10.0.x` | Đảm bảo tính tương thích của token signing & claim parsing. |
| **Microsoft.AspNetCore.DataProtection** | `8.0.10` | `8.0.x` (>= 8.0.10) | `10.0.x` | Bắt buộc giải mã được XML Key Ring cũ. |
| **OpenTelemetry .NET SDK** | `1.9.0` | `1.9.x` (>= 1.9.0) | `1.11.x+` | Kiểm tra không thay đổi metric name và tags. |
| **BCrypt.Net-Next** | `4.0.3` | `4.0.3` | `4.0.3` | Độc lập TFM, tương thích hoàn toàn. |
| **FluentValidation** | `11.10.0` | `11.10.x` | `11.10.x+` | Không đổi cấu trúc validation error dictionary. |
| **Docker Base Images** | `dotnet/sdk:8.0`, `dotnet/aspnet:8.0` | `8.0-bookworm-slim` | `10.0-bookworm-slim` | Đảm bảo curl và glibc tương thích cho non-root user. |
| **PostgreSQL Engine** | `postgres:16-alpine` | PostgreSQL 16.x | PostgreSQL 16.x | **Giữ nguyên PostgreSQL 16**. |

---

## 3. Đề Xuất Cấu Hình Toolchain Cho Giai Đoạn Nâng Cấp

Trong giai đoạn chuẩn bị hiện tại, **chưa áp dụng trực tiếp file cấu hình vào working tree** để tránh gây ảnh hưởng build của IDE (đặc biệt là khả năng đọc giải pháp `Fitmate.slnx`). Nội dung chuẩn bị đề xuất như sau:

### Đề xuất `global.json` (Áp dụng trên upgrade branch):
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

### Đề xuất Dotnet Tool Manifest `.config/dotnet-tools.json`:
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "10.0.0",
      "commands": [
        "dotnet-ef"
      ]
    }
  }
}
```

---

## 4. Các Bất Biến Kiểm Thử Tương Thích (Compatibility Invariants)

Để bảo đảm quá trình nâng cấp không gây ra lỗi ngầm (silent regressions), bộ kiểm thử tương thích phải xác minh 5 bất biến cốt lõi:

1. **Khả năng giải mã của DataProtection (Decryption Backwards Compatibility)**:
   - Dữ liệu mã hóa bằng XML key ring của bản trước đó bắt buộc phải giải mã thành công 100% sau khi nâng cấp.
   - Các trường hợp token bị giả mạo, sai `Purpose` hoặc sai key ring phải bị **từ chối an toàn** (ném `CryptographicException` fail-closed).
2. **Khả năng xác thực mật khẩu (Password Verification & Rehash)**:
   - Mật khẩu đã băm bằng `BcryptPasswordHasher` (work factor 11) và ASP.NET Identity PBKDF2 phải tiếp tục xác thực thành công.
   - Mật khẩu sai phải bị từ chối chính xác.
   - Hỗ trợ cơ chế rehash tự động khi đăng nhập nếu nâng cấp tham số bảo mật.
3. **Tính liên tục của Token & Session**:
   - Token JWT ký từ trước phải parse và validate claims hợp lệ cho đến khi hết hạn.
   - Token đã hết hạn (`exp` trong quá khứ) phải bị từ chối 401 Unauthorized.
   - Refresh token reuse detection hoạt động nhất quán.
4. **Bảo toàn API Wire Contract theo Response Fixtures Thực Tế**:
   - Tuân thủ hợp đồng hiện hành của hệ thống: DTO casing, kiểu dữ liệu, và định dạng ngày giờ.
   - Không áp đặt giả định mới (không bắt buộc mọi chuỗi thời gian phải đổi sang hậu tố Z nếu fixture hiện có chấp nhận timezone offset tương đương).
   - Phản hồi lỗi ProblemDetails (RFC 7807) giữ đúng cấu trúc các trường `status`, `title`, `code`, `errors`, `traceId`.
5. **Tính toàn vẹn của Migration Bundle & PostgreSQL**:
   - Migration bundle (`efbundle`) tự chứa phải build được trên runner Linux glibc.
   - Chạy thử nghiệm bundle trên database cô lập (`fitmate_upgrade_drill`) có chứa sẵn schema và dữ liệu hiện hữu.
   - Không tự ý tạo migration rỗng; nếu EF Core phát hiện model diff, phải phân tích nguyên nhân thay đổi trước khi sinh migration mới.

---

## 5. Kế Hoạch Nâng Cấp và Quản Trị Vận Hành (Upgrade Roadmap)

### Phân công trách nhiệm (Đề xuất — Chờ Operator phê duyệt):
* **Owner đề xuất**: Backend Lead / Lead Architect.
* **Thời hạn đề xuất (Target Deadline)**: **30/10/2026** (trước ngày EOL 10/11/2026 ít nhất 10 ngày để có thời gian theo dõi).

### Các bước thực thi nâng cấp:
1. **Tạo nhánh cô lập**: Tạo branch `upgrade/net10-lts` riêng biệt, không can thiệp nhánh release hiện tại.
2. **Cập nhật SDK & Cấu hình**: Nạp `global.json`, `.config/dotnet-tools.json`, và cập nhật `<TargetFramework>net10.0</TargetFramework>` trong 6 project files.
3. **Cập nhật NuGet Packages**: Nâng cấp các gói Microsoft.* và Npgsql lên phiên bản tương thích .NET 10.
4. **Build & Test Regression**:
   - `dotnet build Fitmate.slnx`
   - `dotnet test tests/Fitmate.UnitTests`
   - `dotnet test tests/Fitmate.IntegrationTests`
5. **Container & Migration Drill**:
   - Build image `fitmate-api:net10` và `fitmate-migrate:net10`.
   - Khởi chạy migration bundle trên container PostgreSQL cô lập (`fitmate_upgrade_drill`).
   - Chạy k6 smoke test kiểm tra latency và throughput.
6. **Kế hoạch Rollback (Phương án quay lui)**:
   - Nếu container .NET 10 gặp lỗi nghiêm trọng tại production/staging: Cắt chuyển ngay về image .NET 8 đã kiểm chứng trước đó (`fitmate-api:net8-stable`).
   - *Điều kiện Rollback*: Bản nâng cấp .NET 10 không được chứa breaking database schema migrations. Nếu có schema change, phải tuân thủ quy tắc Expand/Contract (tương thích ngược hai chiều) để image .NET 8 cũ vẫn đọc ghi bình thường.

