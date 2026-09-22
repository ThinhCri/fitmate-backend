# BE-420 — Storage Adapter (upload media)

> Plan — started 2026-09-22

## 1. Goal

Media upload (avatar, certificate image, post image) với:
1. **Local volume** adapter cho dev (`/uploads` folder, bind-mounted).
2. **HMAC-SHA256 presigned URL** — BE tạo URL có signature + expiry, client GET trực tiếp (không qua BE proxy). TTL ≤ 15 phút.
3. Không gọi cloud SDK (GCS/S3) trong MVP. Interface abstraction để swap later.

## 2. Design

### 2a. Application layer

```
Fitmate.Application/Storage/
├── IStorageService.cs         — UploadAsync(path, stream, contentType) → Uri
├── ISignedUrlService.cs       — CreatePresignedReadUrl(path, ttl) → Uri
└── StorageOptions.cs          — MaxFileSizeBytes, AllowedContentTypes, BasePath
```

```csharp
// IStorageService
Task<Uri> UploadAsync(string relativePath, Stream data, string contentType, CancellationToken ct = default);

// ISignedUrlService
Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl);
```

Validation:
- `AllowedContentTypes`: `["image/jpeg", "image/png", "image/webp"]`
- `MaxFileSizeBytes`: 5 MiB
- Path must start with `"avatars/"`, `"certificates/"`, `"posts/"` (allowlist — prevents path traversal)
- Filename sanitized (alphanumeric + dash + underscore, max 64 chars)

### 2b. Infrastructure layer

```
Fitmate.Infrastructure/Storage/
├── LocalStorageService.cs     — writes to BASE_PATH / relativePath
└── HmacSignedUrlService.cs    — HMAC-SHA256(secret, path + expiry) → base64url signature
```

Signed URL format:
```
{BaseUrl}/{relativePath}?expires={unix_ts}&sig={hmac_base64url}
```

Signature computation:
```
message = "{relativePath}\n{unix_ts}"
sig = HMAC-SHA256(secret, message) → base64url
```

Signature verification:
```
- Check expiry not passed
- Recompute sig from path + expiry → compare (constant-time)
- Return file if valid, 403 otherwise
```

### 2c. API layer

```
POST /api/v1/uploads
  Content-Type: multipart/form-data
  Fields: file (IFormFile), category (string: "avatars" | "certificates" | "posts")
  Auth: [Authorize]
  Response 201: { "url": "https://.../certificates/abc123.jpg?expires=...&sig=..." }

GET /uploads/{**path}           — serve file (no auth for public images)
  403 if sig invalid or expired
  404 if file missing
```

Serve endpoint (static files via ASP.NET Core):
```csharp
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads",
});
```

⚠️ **Security:** `/uploads` should be behind signed URL gate in production. For local dev, allowlist paths only.

### 2d. Options

```csharp
// appsettings.json
"Storage": {
    "BasePath": "./uploads",
    "BaseUrl": "http://localhost:5116/uploads",
    "MaxFileSizeBytes": 5242880,
    "AllowedContentTypes": ["image/jpeg", "image/png", "image/webp"],
    "AllowedCategories": ["avatars", "certificates", "posts"],
    "SignedUrlSecret": "dev-secret-min-32-chars-change-in-prod",
    "SignedUrlTtlMinutes": 15
}
```

### 2e. Certificate wire (BE-420 → BE-090)

`CertificatesController.Upload` → `IStorageService.UploadAsync` → `ICertificateService.SubmitAsync(imageStorageKey)`:

1. User `POST /api/v1/me/certificates/upload` (multipart) → `CertificatesController.Upload`
2. Controller calls `IStorageService.UploadAsync($"certificates/{guid}.{ext}", stream, contentType)`
3. Returns `{ "storageKey": "certificates/guid.ext", "url": "https://.../uploads/certificates/guid.ext?expires=...&sig=..." }`
4. User `POST /api/v1/me/certificates` (body) → includes `imageStorageKey`

## 3. Scope

### In scope
- [x] `IStorageService` + `ISignedUrlService` interfaces
- [x] `LocalStorageService` (local volume, dev)
- [x] `HmacSignedUrlService` (HMAC-SHA256 presigned, TTL ≤ 15 min)
- [x] Storage options + DI registration
- [x] `POST /api/v1/uploads` multipart endpoint
- [x] Static file serve for `/uploads/{path}` with signed-URL gate
- [x] Certificate upload endpoint `POST /api/v1/me/certificates/upload`
- [x] Unit tests (5+): upload success, path validation, signature valid/invalid/expired, category allowlist

### Out of scope
- Cloud adapter (GCS/S3) — swap interface later
- Image processing / thumbnail generation
- Upload DELETE (not needed in MVP)

## 4. Acceptance criteria

- `dotnet build` → 0 errors
- Unit tests pass (storage service + signed URL)
- `POST /api/v1/uploads` with valid file → 201 + signed URL
- `POST /api/v1/uploads` with oversized file → 400
- `POST /api/v1/uploads` with invalid category → 400
- `GET /uploads/path?sig=valid` → 200 file
- `GET /uploads/path?sig=invalid` → 403
- `GET /uploads/path?sig=valid&expires=past` → 403
- Certificate upload flow end-to-end works

## 5. Commit phases

1. `feat(storage): add StorageService and SignedUrlService abstractions + options [BE-420]`
2. `feat(storage): add LocalStorageService and HmacSignedUrlService implementations [BE-420]`
3. `feat(storage): add UploadsController with multipart upload + static file serve [BE-420]`
4. `feat(storage): wire certificate upload flow in CertificatesController [BE-420]`
5. `test(storage): add unit tests for StorageService and HmacSignedUrlService [BE-420]`
6. `docs(storage): add BE-420 plan and DEC-050 decision record [BE-420]`

Total: 6 commits.
