# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

### Phiên 33 — đang chạy (2026-09-21) [BE-170]

**BE-170: Nutrition (log, schedule) + AI stub — DONE**

Plan rev 1 lưu ở `docs/backend-release/tasks/BE-170-plan.md`.

**Phát hiện:** Domain entities + EF config + OpenAPI schemas đã có sẵn. Chỉ cần implement Application layer (service + store + controller + AI stub).

**Commits:**
- **`a278dbc` `feat(be): add Nutrition abstractions, DTOs and error codes [BE-170]`** — INutritionService, NutritionDtos, NutritionErrorCodes.
- **`62678b5` `feat(be): add NutritionService, EF store and AI stub [BE-170]`** — NutritionService, INutritionStore, EfNutritionStore, IAiNutritionAdapter, AiNutritionStubAdapter, NutritionController, DI registration.
- **`b3a8bdd` `test(be): add NutritionServiceTests with 8 cases [BE-170]`** — 8 tests mới.

**Tổng kết:**
- `dotnet build` 0 errors.
- `dotnet test` NutritionServiceTests: 8/8 PASS (375/375 total).
- `NutritionController` 4 endpoints: GET/POST/PATCH /meals + GET/PUT /schedule + POST /scan (10MB limit).
- `AiNutritionStubAdapter` throw `DomainException(AI_UNAVAILABLE)` — trả 503 khi gọi.
- AI stub là default DI; real adapter (BE-430) sẽ override.

**Next:**
- Kiểm tra tasks.md: DONE count 16 (thêm BE-170).
- Chọn task tiếp theo: BE-200 producer wiring (FriendService/ModerationService → push), BE-320 (OpenAPI linter), hoặc BE-330 (k6 load test).


- `tasks.md` BE-130 marked DONE.
- OpenAPI + Controller: đã có đủ, không cần thay đổi.
- Confirm/Accept deferred to BE-060 completion.

**Next:** BE-170 (Nutrition + AI stub) hoặc BE-200 (Notifications) — tùy user.

### Phiên 31 — đã làm (2026-09-21) [BE-160]

**BE-160: Workout + Weight — DONE**

Plan rev 3 lưu ở `docs/backend-release/tasks/BE-160-plan.md`. 8 commit phases đã merged vào `backend-release/contract-and-foundation`:

1. **`fbc474c` `chore(rule): forbid +/&/@ in commit subjects, prefer commas [BE-160]`** — rule commit mới: header-only preferred, ASCII only, comma thay `+` cho separator.

2. **`0353c91` `docs(be): add BE-160 plan rev 3, mark IN_PROGRESS [BE-160]`** — plan rev 3 + tasks.md status IN_PROGRESS.

3. **`3e6a8eb` `feat(be): add WorkoutSessionExercise, Set entities and config [BE-160]`** — 2 entity mới + enum `WorkoutSetStatus` + EF Configuration mapping.

4. **`3091a9b` `feat(be): add WorkoutSession snapshot fields from plan [BE-160]`** — `WorkoutSession` mở rộng 4 snapshot fields (plan_name/day_of_week/planned_duration_min/target_calories_burn).

5. **`27ebfc5` `feat(be): add WorkoutSnapshot migration with pre-check, FK [BE-160]`** — migration `20260920205100_WorkoutSnapshot` tạo 2 table + index UNIQUE (session_exercise_id, set_index), pre-check orphan exercises/sessions trước DDL, bổ sung FK `WorkoutPlanId` SET NULL (sessions) + CASCADE (exercises).

6. **`2544943` `feat(be): add Workout abstractions, DTOs and error codes [BE-160]`** — `IWorkoutService` + `IWeightService` + `WorkoutErrorCodes` + DTOs (`WorkoutSessionView`, `WorkoutSessionDetailView`, `WorkoutSessionExerciseView`, `WorkoutSetView`, `LogWorkoutSessionRequest`, `LogExerciseRequest`, `LogSetRequest`, `LogWeightRequest`).

7. **`c580a50` `feat(be): add WorkoutService, WeightService and EF stores [BE-160]`** — `WorkoutService.LogSessionAsync` idempotent qua (user_id, client_id) + recompute `completed_sets` + outbox `workout.session_logged`. `WeightService` validate 20..400 kg. EF stores `EfWorkoutStore`/`EfWeightStore`. DbContext thêm 2 DbSet.

8. **`575d98b` `feat(be): add WorkoutsController and DI for Workout services [BE-160]`** — `WorkoutsController` `/api/v1/me/workout/sessions` (GET list with from/to filter, GET {id} detail, POST log yêu cầu `Idempotency-Key` header) + `/api/v1/me/weight` (GET/POST). DI bind.

9. **`b63e4dd` `docs(be): extend WorkoutSession OpenAPI schema, add detail endpoint [BE-160]`** — schema mới `WorkoutSessionDetail`, `WorkoutSessionExerciseView`, `WorkoutSessionExerciseInput`, `WorkoutSetView`, `WorkoutSetInput` + endpoint mới `/me/workout/sessions/{id}`.

10. **`444effe` `test(be): add WorkoutServiceTests with 9 cases [BE-160]`** — 9 unit tests PASS: list empty, list filter date range, get detail null for other user, get detail null when not found, log create with snapshot, idempotent by client_id, throws not found plan, throws validation duration, logs outbox event.

**Tổng kết:**
- 0 build errors, 7 pre-existing warnings (NU1603 package downgrade + 1 xUnit1031).
- 9/9 unit tests PASS.
- OpenAPI extensions: 5 schemas mới + 1 endpoint mới.
- Migration: 1 migration mới (`20260920205100_WorkoutSnapshot`).

**DEFERRED:**
- Integration tests (Testcontainers) — project `Fitmate.IntegrationTests` chưa tồn tại; contract test attempt bị Defender Application Control policy block khi load Domain.dll qua reflection (0x800711C7, env-specific). Sẽ triển khai ở phiên sau nếu cần.
- 0353c91 message có `+` vi phạm rule mới — không amend theo rule "never rewrite history without explicit authorization".

**Next:** BE-170 (Nutrition + AI stub) hoặc BE-200 (Notifications v2) — tùy user.

### Phiên 30 — đã làm (2026-09-21) [BE-380]

**BE-300: Docker image + Compose local/integration — PARTIAL_DONE** (1 pre-existing bug phụ thuộc phát hiện).

Plan rev 3 lưu ở `docs/backend-release/tasks/BE-300-plan.md`.

**Phạm vi đã triển khai:**

1. **Dockerfile (api runtime) — `src/Fitmate.Api/Dockerfile`:**
   - Bỏ `COPY Fitmate.sln` / `Fitmate.slnx`; restore + publish trực tiếp qua `Fitmate.Api.csproj` (dotnet tự resolve project refs).
   - Bỏ `COPY tests/` (loại khỏi build context).
   - Runtime stage: `apt-get install curl --no-install-recommends` + `rm -rf /var/lib/apt/lists/*` TRƯỚC `USER fitmate`; pre-create `/home/fitmate/.aspnet/DataProtection-Keys` + chown.
   - HEALTHCHECK + ENTRYPOINT + non-root giữ nguyên.

2. **Dockerfile.dev — `src/Fitmate.Api/Dockerfile.dev`:** sửa tương tự (bỏ COPY Fitmate.sln).

3. **Dockerfile.migrate (mới) — `Dockerfile.migrate`:**
   - Stage 1 (sdk:8.0): `dotnet tool install --global dotnet-ef --version 8.0.10` (pin đúng EF Core runtime 8.0.10); restore + copy src/; build self-contained bundle linux-x64 (`--runtime linux-x64 --self-contained`).
   - Stage 2 (runtime-deps:8.0-bookworm-slim): non-root fitmate UID 1001 + HOME=/tmp + `DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/bundle-extract` (self-contained bundle cần writable extract dir).

4. **Compose — `compose.yml`:**
   - Bỏ `container_name` cố định (Compose tự prefix theo `--project-name`).
   - Bỏ default ports (chỉ bind qua override); compose.verify300.yml bind riêng 15432/11025/18025/18080.
   - api thêm volume `fitmate-dpkeys:/home/fitmate/.aspnet/DataProtection-Keys`.
   - migrate profile dùng `Dockerfile.migrate` (image ENTRYPOINT, KHÔNG command override).

5. **Verify override — `compose.verify300.yml` (mới):** cổng host riêng + volume named riêng để chạy song song stack local.

6. **DataProtection LOCAL persistence — `src/Fitmate.Api/Program.cs`:** `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/home/fitmate/.aspnet/DataProtection-Keys")).SetApplicationName("Fitmate.Api")` + `using Microsoft.AspNetCore.DataProtection;`. Production key (secret manager / certificate) → BE-340.

7. **Package — `src/Fitmate.Api/Fitmate.Api.csproj`:** thêm `Microsoft.AspNetCore.DataProtection.Extensions 8.0.10` (extension method namespace).

8. **.dockerignore:** thêm `tests/`, `tasks/`.

9. **Runbook — `docs/backend-release/runbooks/local-dev.md` (mới):** prereq + 7 bước quickstart + verify recipe tách biệt + cleanup + known limitations + security warning về `docker compose config` không mask secret.

**Evidence (chạy 2026-09-20):**

| Bước | Lệnh | Kết quả |
|------|------|---------|
| Build api image | `docker compose -p fitmate-verify300 build api` | exit 0, image `fitmate-verify300-api:latest` (~526 MB). |
| Build migrate image | `docker compose -p fitmate-verify300 --profile migrate build migrate` | exit 0, image `fitmate-verify300-migrate:latest` (~101 MB). |
| Compose up postgres+mailhog | `docker compose -p fitmate-verify300 ... up -d postgres mailhog` | 2 container up, postgres healthy trong ~5s. |
| Migrate empty DB | `docker compose -p fitmate-verify300 --profile migrate run --rm migrate` | exit 0; apply 6 migrations theo thứ tự: `InitialSchema` → `IdentitySchema` → `ModerationAction` → `FriendshipAndBlock` → `NotificationsPushV2` → `ChatFriendV1`. Done. |
| Schema verify | `docker exec fitmate-verify300-postgres-1 psql -U fitmate -d fitmate -c "\dt"` | **44 tables** (Identity 6 + domain 38 + `__EFMigrationsHistory`). |
| Re-migrate idempotent | run migrate lần 2 cùng volume | exit 0; "No migrations were applied. The database is already up to date." |
| dotnet test host | `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-restore` | **350/350 PASS**, 0 failed, 0 skipped, 4s. |
| Cleanup | `docker compose -p fitmate-verify300 down -v` | xoá containers + 2 named volume verify300-*. |

**Pre-existing bug phát hiện (KHÔNG thuộc BE-300, chặn verify API readiness):**

Khi up api container, log crash liên tục:
```
Unhandled exception. System.InvalidOperationException: JwtOptions.SigningKey must be configured (env JWT_SIGNING_KEY)
   at Program.<Main>$(String[] args) in /src/src/Fitmate.Api/Program.cs:line 248
```
Sau khi rename env `JWT_SIGNING_KEY` → `Jwt__SigningKey` (đúng convention ASP.NET Core config mapping), crash tiếp:
```
System.InvalidOperationException: Unable to resolve service for type 'Fitmate.Application.Common.JwtOptions'
while attempting to activate 'Fitmate.Application.Auth.RefreshTokenService'.
```
Hai bug:
1. `.env.example` dùng `JWT_SIGNING_KEY` nhưng `Program.cs` dòng 245 đọc `builder.Configuration.GetSection("Jwt").Get<JwtOptions>()` → ASP.NET Core map `JWT_SIGNING_KEY` không match `Jwt:SigningKey` (cần `Jwt__SigningKey`).
2. `JwtOptions` không được register DI (chỉ `Configure<JwtOptions>` bind, không `AddSingleton`/`AddScoped`).

**Đề xuất task mới BE-380: Config/JWT_OPTIONS wiring fix** — sửa env key alias (chấp nhận cả 2 tên) + register JwtOptions DI. CHƯA xử lý — nằm ngoài scope BE-300.

**Không thuộc BE-300 — đã defer có task ID:**

- BE-310 GitHub Actions CI.
- BE-320 OpenAPI linter + contract test.
- BE-330 Load test scripts (k6).
- BE-340 Secrets management (gitleaks + DataProtection prod key + secret manager).

**Chưa có task ID — ghi pending:**

- **BE-350**: Auto-migrate on startup với pg_advisory lock (chỉ khi multi-instance api) — không bắt buộc, migration job vẫn hợp lệ.
- **BE-360**: Backup/restore runbook (PostgreSQL volume + DataProtection keys).
- **BE-370**: Test infrastructure để verify DataProtection key ring tự động (dev-only endpoint).
- **BE-380**: JwtOptions config wiring fix (env alias + DI registration) — phát hiện trong BE-300 verify, chưa thuộc task nào.

**Verify recipe end-to-end bị gián đoạn ở bước 6 (api readiness)** do BE-380. Các bước 1-5 (build, postgres up, migrate, re-migrate, schema verify, host test) đều PASS.

**Commit chưa thực hiện:** 2 message đã duyệt, đợi xác nhận cuối từ user.
1. `chore(docker): fix builds and migration workflow [BE-300]`
2. `docs(docker): document local setup and verification [BE-300]`

**CẬP NHẬT 2026-09-21:** commit 1 + 2 đã thực hiện (`33c18f0` + `253448d`), handoff pending update.

### Phiên 30 — đã làm (2026-09-21) [BE-380]

**BE-380: Config/JWT_OPTIONS wiring fix** — DONE.

Phát hiện trong BE-300 verify: api container crash liên tục vì `JwtOptions.SigningKey` rỗng (env `JWT_SIGNING_KEY` không map sang `Jwt:SigningKey`), và `RefreshTokenService` inject raw `JwtOptions` nhưng không register DI.

**Triển khai:**
- **`src/Fitmate.Application/Common/JwtOptionsLoader.cs` (mới):** helper load + validate `JwtOptions` tại một nơi duy nhất.
  - Env canonical: `JWT_SIGNING_KEY` (đã dùng trong `.env.example` + compose).
  - Alias ASP.NET Core: `Jwt__SigningKey`.
  - Section: `Jwt:SigningKey`.
  - **Một trong ba set** → dùng giá trị đó. **Cả ba set và giống nhau** → chấp nhận. **Khác nhau** → throw fail-fast, không chọn ngầm. **Thiếu** hoặc **key < 32 bytes UTF-8** → throw fail-fast, không fallback default. Không log giá trị key.
  - Issuer/Audience/TTLs đọc từ section `Jwt:*` (backward-compat).
- **`src/Fitmate.Api/Program.cs`:** dùng `JwtOptionsLoader.Load(builder.Configuration)` ngay sau khi bind `AppOptions`. Register raw instance `AddSingleton(jwtOptions)`. `IJwtTokenService` + `IRefreshTokenService` factories cùng resolve instance này (RefreshTokenService giờ resolve được DI — bug 2). Bearer middleware dùng cùng instance. Loại bỏ khối `GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions()` rồi check empty — việc validate đã chuyển cho loader.
- **`src/Fitmate.Application/Fitmate.Application.csproj`:** thêm `Microsoft.Extensions.Configuration.Abstractions` 8.0.0 + `Microsoft.Extensions.Configuration.Binder` 8.0.0 (loader cần `IConfigurationSection.Get<T>()`).
- **`tests/Fitmate.UnitTests/JwtOptionsLoaderTests.cs` (mới, 13 tests):** only canonical / only alias / only section / canonical+alias equal / canonical+alias mismatch (conflict message) / all-three equal / no key (missing) / key 31/16/1 bytes (too short) / key 32 bytes (boundary OK) / null configuration / keeps issuer/audience/TTLs from section.
- **`docs/backend-release/runbooks/local-dev.md`:** sửa dòng 24 — thay "Code map `Jwt:SigningKey` ← `JWT_SIGNING_KEY`" bằng "Code dùng `JwtOptionsLoader` hỗ trợ cả `JWT_SIGNING_KEY` (canonical) và `Jwt__SigningKey` (alias)".
- **`src/Fitmate.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`:** cập nhật comment cũ (ghi "Program.cs throw nếu JWT_SIGNING_KEY rỗng" — không còn đúng).

**Kết quả:**
- `dotnet build`: 0 error.
- `dotnet test`: **363/363 PASS** (13 mới JwtOptionsLoader + 350 cũ).
- Commit: `84b61da fix(auth): JwtOptions dual-alias load, DI, validation [BE-380]` — 6 files, +282/-15.
- Branch: `backend-release/contract-and-foundation`. Chưa push (per AGENTS).

**Verify recipe tiếp theo (BE-300 follow-up):** chạy `docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d postgres mailhog` rồi `docker compose ... --profile migrate run --rm migrate` đã PASS (phiên 29). Tiếp theo `up api` (cần `.env` có `JWT_SIGNING_KEY`) → `/health/ready` 200 → smoke `POST /api/v1/auth/login`. Nếu thành công → BE-300 → DONE, mở BE-310 (GitHub Actions CI).

### Phiên 31 — đã làm (2026-09-21) [BE-310]

**BE-310: GitHub Actions CI** — DONE (definition only, chưa chạy thật vì cần UA-002 repo GitHub public).

**Triển khai:**
- **`.github/workflows/ci.yml` (mới):** 3 jobs gate độc lập.
  - `lint-test`: `actions/checkout@v4` + `actions/setup-dotnet@v4` (DOTNET_VERSION=8.0.x). `dotnet restore Fitmate.slnx` → `dotnet build` với `-warnaserror` + `/p:NoWarn=xUnit1031` (loại warning cosmetic duy nhất). `dotnet test` ghi TRX artifact upload qua `actions/upload-artifact@v4`.
  - `docker-build` (needs `lint-test`): `docker/setup-buildx-action@v3` + `actions/cache@v4` (path `/tmp/.buildx-cache`, key `runner.os-buildx-sha`). Build 2 image (`src/Fitmate.Api/Dockerfile` → `fitmate-ci-api`, `Dockerfile.migrate` → `fitmate-ci-migrate`) với `--cache-from`/`--cache-to` local. **Không push** image (UA-002/003 + BE-500/340 pending).
  - `smoke` (needs `docker-build`): verify `docker compose version`, generate ephemeral `JWT_SIGNING_KEY` qua `openssl rand -base64 64` + ghi `.env` (DB_PASSWORD/JWT_SIGNING_KEY/FEATURE_*). `docker compose up -d postgres` → wait `(healthy)` 60s → `--profile migrate run --rm migrate` → `up -d api` → poll `http://127.0.0.1:8080/health/ready` 90s (`SECONDS` elapsed counter, curl fail log `docker compose logs api` rồi exit 1). Smoke `/health/live` 200. `if: always()` teardown `docker compose down -v` + rm `.env`.
  - `on: push/PR branches: [main, 'backend-release/*']`.
- **`docs/backend-release/runbooks/ci.md` (mới):** 8 sections — workflow structure, trigger rules, secrets & ephemeral, local preview qua `act`, tại sao chưa push image (UA-002/003 + BE-500/340), lỗi thường gặp (NU1603 drift / EF version / readiness timeout), branch protection recommend (gồm 3 required checks), files map.

**Kết quả:**
- YAML: structure 2-space indent, multi-line shell scripts dùng `|` block, không có anchor/alias phức tạp. Không có yaml parser local (Python/Node `yaml` package không cài) — verified manual review + cross-check với GitHub Actions schema docs.
- Chưa chạy workflow thật (cần UA-002 repo public trên GitHub + runner có quyền `contents: read`). Khi có UA-002, branch protection rule theo runbook §7 sẽ require 3 jobs PASS trước merge.

**Commit:**
- (sẽ commit riêng 2 message: ci.yml + docs, theo policy BE không auto-commit)

**Local verify (sau commit):**
- Cài `act v0.2.89` qua winget. Set `actrc` map `ubuntu-22.04 → node:20-bookworm-slim` + `--container-architecture linux/amd64` + `DOCKER_HOST=npipe:////./pipe/dockerDesktopLinuxEngine`.
- `act -j lint-test -n` (dry-run) → PASS — 6 step nhận diện đầy đủ (Checkout/Setup .NET 8.0.x/Restore/Build/Test/Upload artifact), triggers + env + secrets mapping đúng.
- `act -j lint-test` (real run) → PASS 4/6 step (Checkout/Setup .NET 8.0.425 installed/Restore fail), fail ở bước `dotnet restore Fitmate.slnx` do image thiếu `libicu` (system lib cần cho `System.Globalization`). GitHub runner thật (`ubuntu-22.04`) có sẵn libicu → workflow chạy đầy đủ trên GH. Reverted phần thử cài `apt-get install libicu` (chỉ phụ thuộc act image, không cần trên GH runner).
- Ghi nhận `act` chỉ verify workflow structure / triggers; verify end-to-end cần GitHub runner thật.

### Phiên 28 — đã làm (2026-09-20) [BE-191]

**BE-191: Chat (friend message) — friend-only scope** — DONE.

Triển khai 5 friend chat endpoints trong OpenAPI. AI chat KHÔNG thuộc scope này (BE-430 chờ provider); schema AI thread giữ nguyên để không vướng. User chốt scope ở AskQuestion:
- Friend-only (5 endpoints hiện có, không AI).
- `peer_email` → `peer_user_id` (DEC-036 đồng bộ).
- 1 conversation canonical cho mỗi cặp user, CHECK low<high, không cascade delete với Friendship.
- `ChatThreadRead` riêng (unique `(thread_id, user_id)`), `last_read_message_id` + thời điểm server xác định, monotonic.
- `client_message_id` cho idempotency retry, marker không lùi khi nhiều thiết bị.
- Block/unfriend KHÔNG xoá history; chỉ chặn send mới.

**Triển khai:**
- **Domain (commit 1):** `FriendConversation(UserLowId, UserHighId, LastMessageAt)` với `CanonicalPair` helper (Guid.CompareTo lexical byte order, đồng bộ `Friendship.CanonicalPair`), `IsParticipant` + `OtherParticipant` helpers. `ChatThreadRead(ConversationId, UserId, LastReadMessageId, LastReadAt)` với PK composite + `TryAdvance` (chỉ update khi có advance). `ChatMessage` extend thêm `ConversationId` (nullable, null = AI thread) + `ClientMessageId` (nullable, idempotency). Migration `20260920121331_ChatFriendV1`: alter `chat_messages.ThreadId` → nullable; thêm `chat_messages.ConversationId` (nullable) + `chat_messages.ClientMessageId` (nullable); tạo `friend_conversations` (CHECK `UserLowId<UserHighId`, UNIQUE pair) + `chat_thread_reads` (PK composite); indexes `(UserHighId)`, `(ConversationId+CreatedAt+Id)`, partial unique `(ConversationId+ClientMessageId) WHERE ConversationId IS NOT NULL AND ClientMessageId IS NOT NULL`. Apply OK trên local Postgres.
- **Application (commit 2):** `IChatStore` abstraction (GetOrCreate/FindConversation/FindConversationForPair/AddMessage/FindMessageByClientMessageId/GetLastMessage/ListConversations với aggregate ConversationSummaryRow/ListMessages/FindMessage/GetThreadRead/UpsertThreadRead/CountUnread/SaveChanges). `IChatService` (SendFriendMessage/ListConversations/ListMessages/MarkRead/GetConversation) + DTOs (`ChatMessageDto`, `FriendSummaryDto`, `ConversationDto`, `ConversationPage`, `MessagesPage`). `ChatService` inject `IChatStore`, `IFriendService` (qua `IsFriendAsync`), `IBlockStore` (`IsBlockedEitherWayAsync`), `IOutboxDispatcher`, `TimeProvider`. Send flow: validate (self 400 / empty/long 400 / auth 401) → block check 2 chiều → friend check → GetOrCreate conversation (race-safe qua UNIQUE constraint) → dedup check (same client_message_id + same payload → trả row cũ; khác payload → 409 `IDEMPOTENCY_MISMATCH`) → add message + outbox `chat.message.sent` (cùng transaction). MarkRead: monotonic (`candidate_message.CreatedAt > current.LastReadAt`); cũ hơn hoặc trùng → no-op 204. Cursor pagination: `(last_message_at desc, id desc)` cho conversations, `(created_at desc, id desc)` cho messages. Limit 1..100, default 30.
- **Infrastructure (commit 3):** `EfChatStore`: `GetOrCreateConversationAsync` với catch `DbUpdateException` cho race (request thứ 2 retry qua `FindConversationForPairAsync`); `ListConversationsAsync` aggregate query 1 round-trip (conversations + last_messages GroupBy + reads map + unread count); `UpsertThreadReadAsync` update in-place. Pagination clamp + cursor encode/decode base64(ticks|guid).
- **API (commit 4):** `ChatController` (`POST /api/v1/chat/friends/messages` body `{peer_user_id, text, client_message_id?}` → 201 `ChatMessageDto`), `GET /api/v1/chat/friends/conversations?cursor&limit`, `GET /api/v1/chat/friends/conversations/{id}`, `GET /api/v1/chat/friends/conversations/{id}/messages?cursor&limit`, `POST /api/v1/chat/friends/conversations/{id}/read` body `{last_read_message_id}` → 204. DI wiring `IChatStore` + `IChatService` trong `Program.cs`.
- **Docs (commit 5):** OpenAPI: schema `ChatMessage` + `Conversation` extend (thêm `conversation_id`, `client_message_id`, `last_message_at`); schema mới `MarkReadRequest`; `peer_email` → `peer_user_id` (DEC-036); thêm 3 endpoints mới (conversations/{id}, messages, read). Authorization matrix §9 cập nhật (participant check 404, monotonic, idempotency). Domain-rules §12 mới (friend chat policy: canonical, idempotency, authorization, read marker monotonic, unread count, history retention, pagination, privacy). Decisions DEC-042 (canonical pair), DEC-043 (idempotency client_message_id), DEC-044 (read marker monotonic), DEC-045 (block/unfriend không xoá history) — Accepted. Feature-api-matrix FP-CH-04..07 cập nhật status.
- **Tests (commit 6):** `ChatServiceTests.cs` mới — **29/29 PASS** (full suite 350/350 từ 321). Coverage: permission (self/block 2 chiều/non-friend/empty/long/auth), idempotency (same payload/same key + different payload → 409), conversation lifecycle (canonical pair khi 2 user cùng tạo, unfriend preserved, block prevent send + history kept), read marker (advance/no-op old/non-participant 404/wrong message 400/unknown 400), unread count (own messages excluded, decreases after mark-read, increases on new messages after marker), pagination (limit clamp, empty conversation), GetConversation (participant/non-participant 404), outbox same-transaction, FindConversationForPair reverse-order.

**Kết quả:**
- `dotnet build`: 0 warning, 0 error (1 pre-existing xUnit1031 trong `AuthorizationGuardTests.cs:37` không liên quan).
- `dotnet test`: **350/350 PASS** (29 mới + 321 cũ; 0 failed, 0 skipped).
- Migration `ChatFriendV1` apply OK trên local Postgres (32 → 35 tables).
- OpenAPI YAML update — schema ChatMessage + Conversation extend + MarkReadRequest schema + 3 endpoints mới (conversations/{id}, messages, read) + peer_user_id.

**Files changed (BE-191):**
- Domain: `src/Fitmate.Domain/Chat/FriendConversation.cs` (new), `src/Fitmate.Domain/Chat/ChatThreadRead.cs` (new), `src/Fitmate.Domain/Chat/ChatMessage.cs` (extend ConversationId + ClientMessageId), `src/Fitmate.Domain/Common/ErrorCodes.cs` (+Chat section).
- Application: `src/Fitmate.Application/Chat/IChatStore.cs` (new), `src/Fitmate.Application/Chat/IChatService.cs` (new + DTOs), `src/Fitmate.Application/Chat/ChatService.cs` (new).
- Infrastructure: `src/Fitmate.Infrastructure/Persistence/Chat/EfChatStore.cs` (new), `src/Fitmate.Infrastructure/Persistence/Configurations/ChatConfiguration.cs` (extend — FriendConversationConfig + ChatThreadReadConfig + ChatMessage partial unique), `src/Fitmate.Infrastructure/Persistence/FitmateDbContext.cs` (+2 DbSet), `src/Fitmate.Infrastructure/Persistence/Migrations/20260920121331_ChatFriendV1.cs` + `.Designer.cs`, `FitmateDbContextModelSnapshot.cs` (auto).
- Api: `src/Fitmate.Api/Chat/ChatController.cs` (new — 5 endpoints), `src/Fitmate.Api/Program.cs` (+using +DI).
- Tests: `tests/Fitmate.UnitTests/ChatServiceTests.cs` (new — 29 tests).
- Docs: `docs/backend-release/openapi/fitmate-v1.yaml` (ChatMessage + Conversation + MarkReadRequest + 3 endpoints mới + peer_user_id), `docs/backend-release/decisions.md` (DEC-042..045), `docs/backend-release/domain-rules.md` (§12 mới), `docs/backend-release/authorization-matrix.md` (§9 update), `docs/backend-release/feature-api-matrix.md` (FP-CH-04..07), `docs/backend-release/tasks.md` (BE-191 → DONE), `docs/backend-release/handoff.md` (phiên 28 này).

**Pending integration (ghi tasks.md, không block):**
- BE-200 worker phase 2: consume outbox `chat.message.sent` → push notification cho peer. Schema `ux_notifications_dedup` đã handle idempotent. Cần wire handler khi BE-200 mở rộng.
- BE-310 Integration test WebApplicationFactory sẽ cover route 200/401/403/404 cho 5 endpoint này.
- BE-160 Booking confirm/accept (PARTIAL_DONE ở BE-060).
- AI chat (BE-430 chờ provider).

### Phiên 27 — đã làm (2026-09-20) [BE-200]

**BE-200: Notifications — Phase 1 (in-app + push delivery abstraction)** — PARTIAL_DONE.

Đã implement:
- **Domain (commit 1):** `Notification` (Summary, SourceEventId, SourceType), `Device` (EncryptedToken, KeyId, Provider, IsActive, RevokedAt, LastUsedAt), `PushDelivery`, `PushProviderConfig` + enums `PushProvider{Fcm, Expo}` và `PushDeliveryStatus` (8 states: Pending/Sent/Deferred/FailedRetryable/FailedPermanent/TokenInvalid/Sweeping/Expired).
- **Application (commit 2):** `INotificationService`, `IDeviceService`, `IPushDeliveryDispatcher`, `IPushProvider`, `IPushDeliveryTokens`, `INotificationStore`, `IDeviceStore`, `IPushStore`, `INotificationAuditLogger`, `NotificationsDtos`, `PushPayload`, `PushTtlOptions`.
- **Application services (commit 3):** `NotificationService` (List + MarkRead idempotent, 404 cho not-owned), `DeviceService` (Register idempotent SHA-256 dedup + Revoke soft), `PushDeliveryDispatcher` (DispatchAsync idempotent qua `ExistsBySourceAsync`; skip push + audit `notification.skipped reason=push_disabled|no_active_device`; nếu enabled → tạo `PushDelivery` per active device + enqueue outbox `notification.push_requested`). `IPushDeliveryTokens` simplify chỉ 2 method `Protect`/`Unprotect` (DataProtection tự nhúng key id trong ciphertext → không cần expose key id ở Application).
- **Application outbox (commit 4):** `NotificationPushHandler` (Type=`notification.push_requested`): per-device sender-side checks (active, owner match, TTL expired, domain validity + block check cho friend source), per-device failure isolation, decrypt qua `IPushDeliveryTokens.Unprotect`, gọi `IPushProvider.SendAsync`, map `PushSendResult` → status transitions. `PushDeliverySweepService` claim expired qua `FOR UPDATE SKIP LOCKED` rồi mark `Sweeping` → `Expired`.
- **Infrastructure (commit 5):** `EfNotificationStore` / `EfDeviceStore` / `EfPushStore` (raw SQL claim), `EfPushDeliveryDispatcher` (DbContext save), `DataProtectionTokenCipher` (`IDataProtectionProvider.CreateProtector("fitmate.push-token.v1")`, log cảnh báo khi decrypt fail → caller treat `TokenInvalid`), `MissingPushProvider` (luôn trả `ProviderNotConfigured`), `NotificationAuditLogger` (Serilog structured), `SweepBackgroundService` (Interval=30 phút). csproj thêm `Microsoft.AspNetCore.DataProtection` + `Microsoft.Extensions.Hosting.Abstractions`. DbContext thêm `DbSet<PushDelivery>` + `DbSet<PushProviderConfig>`.
- **EF Configuration (commit 6):** `NotificationsConfiguration` cho 4 table. Indexes: `ix_notifications_user_created`, `ix_notifications_user_read_created`, partial unique `ux_notifications_dedup (user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL`. Devices: unique `ux_devices_user_token (user_id, token_hash)`, partial `ix_devices_active WHERE is_active=true`. Push deliveries: partial `ix_push_deliveries_sweep WHERE status IN (0,2,3)`, ix notification_id, ix device_id. NotificationPushHandler refactor: bỏ `IBlockLookup` riêng, dùng `IBlockStore` từ BE-181.
- **API + DI (commit 7):** `NotificationsController` (`GET /api/v1/me/notifications?read=&page=&pageSize=`, `POST /api/v1/me/notifications/{id}/read`). `DevicesController` (`POST /api/v1/users/me/devices` body `{device_token, platform, provider}`, `DELETE /api/v1/users/me/devices/{id}`). Program.cs DI: `IPushDeliveryTokens` (scoped), 3 EF stores, 2 services, `INotificationAuditLogger`, `IPushDeliveryDispatcher` (factory inject DbContext), `IPushProvider` (singleton `MissingPushProvider`), `PushDeliverySweepService` (scoped) + `SweepBackgroundService` (hosted), `NotificationPushHandler` outbox handler. DataProtection comment chỉ đến UA-DP-001 (secret manager cho key ring).

**Pending trong BE-200:** — DONE 2026-09-20 (phiên 27 cập nhật, xem "Phiên 27 update" bên dưới).
- Producer wiring: FriendService accept path → enqueue notification (gắn với source_type=`friend`); ModerationService.Warn → enqueue notification (gắn với source_type=`moderation`). ✅
- Unit tests: NotificationService (list filter + MarkRead idempotent + 404), DeviceService (register idempotent + revoke 404 + validation), PushDeliveryDispatcher (skip khi disabled / khi không có device / fan-out), NotificationPushHandler (block check, device inactive, expired, result mapping), PushDeliverySweepService. ✅
- Migration generate + apply local: `dotnet ef migrations add NotificationsPushV2 --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api`. ✅ (migration `20260920064150_NotificationsPushV2`)
- OpenAPI doc update: thêm `/me/notifications` + `/users/me/devices` + schemas. ✅ (đã có sẵn từ phase trước — endpoints + `Notification` schema)
- DEC-040 (key ring location) + DEC-041 (sweep interval): document trong `decisions.md`. ✅ (status Proposed → Accepted)
- `Authorization matrix` cập nhật §10 (device owner check + Producer dedup). ✅
- Producer tests: đảm bảo mỗi domain event trigger đúng source_event_id (không trùng). ✅ (FriendServiceTests + ModerationServiceTests cập nhật)

**Phiên 27 update (2026-09-20)** — BE-200 PARTIAL_DONE hoàn tất:
- **Migration `20260920064150_NotificationsPushV2`**: apply OK trên local Postgres. Schema verified: 4 bảng (`notifications`, `devices`, `push_deliveries`, `push_provider_configs`) với 5 indexes.
- **Producer wiring**: `FriendService.SendRequestAsync` + `AcceptRequestAsync` gọi `IPushDeliveryDispatcher.DispatchAsync` với `source_event_id = request.Id`, `source_type = friend_request_sent | friend_request_accepted`. `ModerationService.ResolveReportAsync` (action=Warn) với `source_event_id = report.Id`, `source_type = moderation_warning`. Partial unique index `(user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL` đảm bảo idempotent.
- **Unit tests**: `dotnet test` **321/321 PASS** (76 mới: NotificationService 11 + DeviceService 9 + PushDeliveryDispatcher 12 + NotificationPushHandler 14 + PushDeliverySweepService 5 + 25 cập nhật trong FriendServiceTests/ModerationServiceTests).
  - Domain fix: `PushDelivery.CreateExpired` `internal` → `public` để test project truy cập.
  - Test mock: `NotificationPushHandlerTests.InMemoryDeviceStore.GetActiveDevicesAsync` trả tất cả devices (handler tự enforce per-device checks).
- **Docs**: DEC-040 + DEC-041 → **Accepted**. Authorization matrix §10 bổ sung "Producer dedup" note.
- **Commit**: `e864408 test(notifications): add unit tests for ... [BE-200]`.

**Pending beyond BE-200:**
- UA-FCM-001: chọn push provider thật (FCM trực tiếp vs Expo). Hiện dùng `MissingPushProvider` (luôn `ProviderNotConfigured`, không backlog tích lũy).
- UA-DP-001: secret manager cho DataProtection key ring (path trên production).
- BE-191 Chat (friend message) đang phụ thuộc BE-181 (đã xong) — bắt đầu triển khai tiếp.
- BE-160 Status migration, BE-130 booking flows, BE-140 payment skeleton.

### Phiên 26 — đã làm (2026-09-19) [BE-151]

**BE-151: GET /admin/platform-policy (read-only theo DEC-029)** — DONE.

Triển khai endpoint read-only để Admin Web (`Settings.jsx`) hiển thị chính sách phí nền tảng cố định 10%, đồng thời cung cấp foundation cho FE convert sang % và policy_version cho audit.

**Triển khai:**
- `PlatformPolicyOptions` (Application/Common): bind section `PlatformPolicy` qua env `PLATFORM_POLICY__FEEBPS/POLICYVERSION/EFFECTIVEFROMUTC`. Default 1000 bps / "v1" / 2026-09-19 UTC. Bps ∈ [0,10000] validate qua `Money.ValidateBps`.
- `IPlatformPolicyService` + `PlatformPolicyService` + record `PlatformPolicy` (PlatformFeeRateBps, PolicyVersion, EffectiveFrom, Editable=false, DisplayHint). Service enforce role Admin qua `IAuthorizationGuard.EnsureRoleAsync` (BE-090) — chống bypass controller filter.
- `AdminPlatformPolicyController` (`/api/v1/admin/platform-policy`): `[Authorize(Policy=AdminOnly")]` + route `[HttpGet]` chỉ.
- `Program.cs`: bind section + register service.
- `.env.example` + `.env`: thêm block `# ===== Platform policy (DEC-029 read-only) =====` với default 10%.
- `tasks.md` BE-151 → DONE.

**Kết quả**:
- `dotnet build`: 0 error (1 warning pre-existing xUnit1031 trong `AuthorizationGuardTests.cs:37`, không liên quan).
- `dotnet test`: **275/275 PASS** (9 mới + 266 cũ; 0 failed, 0 skipped). Note: `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` được ghi pre-existing fail ở phiên 24 không còn fail khi chạy sau BE-151 — có thể do build cache refresh. Đã verify bằng cách chạy lại full test suite.
- OpenAPI spec cho endpoint đã có sẵn (BE-150 scope mở rộng), không cần update.

**Files changed**:
- Domain/Application: `src/Fitmate.Application/Common/PlatformPolicyOptions.cs` (new), `src/Fitmate.Application/Finance/PlatformPolicyService.cs` (new — interface + record + impl), `src/Fitmate.Application/Common/AppOptions.cs` (+PlatformPolicy property).
- Api: `src/Fitmate.Api/Admin/AdminPlatformPolicyController.cs` (new), `src/Fitmate.Api/Program.cs` (Configure section + DI).
- Tests: `tests/Fitmate.UnitTests/PlatformPolicyTests.cs` (new — 9 tests).
- Docs: `docs/backend-release/tasks.md` (BE-151 entry + DONE summary update), `docs/backend-release/handoff.md` (phiên 26 này).

**Pending integration (ghi tasks.md, không block)**:
- BE-310 Integration test WebApplicationFactory sẽ cover route 200 + 401/403 cho endpoint này.
- Post-MVP: `POST /admin/platform-policy` cho phép admin sửa (DEC-029 §"Khi scale") — cần `platform_policies` table + audit + version + cool-down. Chưa có trong release đầu.

### Phiên 25 — đã làm (2026-09-19) [BE-181]

**BE-181: Friend + Friendship + Block** — DONE.

Triển khai đúng 6 quyết định user chốt 2026-09-19 (ghi `decisions.md` DEC-036..039):
1. **Recipient dùng `to_user_id`** (UUID, không email). Sender lấy từ authenticated principal. OpenAPI đổi `to_email` → `to_user_id`. Migration rename `FromUserId→RecipientId`, `ToUserId→SenderId`.
2. **Friendship: 1 row canonical** `(UserLowId, UserHighId)` với `CHECK (low<high)`. Application dùng `Friendship.CanonicalPair(a, b)` chuẩn hoá trước khi insert. .NET `Guid.CompareTo` và PostgreSQL `<` đều so sánh UUID theo value byte order — khớp nhau.
3. **Unfriend: hard delete** Friendship row canonical. Idempotent nếu không phải bạn. KHÔNG xoá chat / post / booking kèm theo. Outbox `friendship.removed` không gửi notification.
4. **Block: bảng `blocked_users` riêng**, tách khỏi moderation. Block huỷ pending FriendRequest 2 chiều + xoá Friendship hiện tại. Unblock KHÔNG tự khôi phục. KHÔNG gửi notification cho người bị block. KHÔNG ảnh hưởng booking/finance.
5. **Outbox events** chuẩn hoá: `friend.request.created/accepted` enqueue; `rejected/cancelled/friendship.removed/user.blocked/user.unblocked` KHÔNG notification.
6. **Accept atomic**: 1 transaction — status guard + block re-check (race) + insert Friendship + outbox. Race accept vs block → reject silent, không tạo friendship.

**Kết quả**:
- `dotnet build`: 0 error, 1 warning pre-existing (`xUnit1031` không liên quan).
- `dotnet test`: **266/266 PASS** (47 mới + 219 cũ; 0 failed, 0 skipped).
- Migration `20260919111325_FriendshipAndBlock` apply OK trên local Postgres. Schema verify: drop `friends`, create `friendships` + `blocked_users` với CHECK constraint, partial unique index `(sender_id, recipient_id) WHERE "Status"=0`.

**Files changed**:
- Domain: `src/Fitmate.Domain/Social/Friendship.cs` (new — canonical pair + helper), `Social/BlockedUser.cs` (new), `Social/FriendRequest.cs` (rename fields + Cancel), `Common/Enums.cs` (+Cancelled), `Common/ErrorCodes.cs` (+9 codes).
- Application: `Social/IFriendStore.cs` (new), `Social/IFriendService.cs` (new — FriendPage/BlockListPage/FriendRequestStatusView), `Social/FriendService.cs` (new — 7 use cases), `Social/BlockService.cs` (new), `Social/CursorEncoder.cs` (new — share với BE-180), `Social/ISocialService.cs` + `Social/SocialService.cs` (delegate `IsFriendAsync` qua `FriendService`).
- Infrastructure: `Social/EfFriendStore.cs` (new — canonical query + cursor pagination), `Social/EfBlockStore.cs` (new), `Social/EfSocialStore.cs` (Friends-only feed query canonical), `Persistence/Configurations/SocialConfiguration.cs` (FriendRequestConfiguration + FriendshipConfiguration + BlockedUserConfiguration), `Persistence/FitmateDbContext.cs` (DbSet).
- Api: `Social/FriendsController.cs` (new — 6 endpoints), `Social/BlocksController.cs` (new — 3 endpoints), `Program.cs` (DI).
- Tests: `tests/Fitmate.UnitTests/FriendServiceTests.cs` (new — 20 tests), `tests/Fitmate.UnitTests/SocialServiceTests.cs` (add `StubFriendService`).
- Migrations: `Persistence/Migrations/20260919111325_FriendshipAndBlock.cs` (new — drop `friends`, create `friendships` + `blocked_users`, rename columns, partial unique index), `Migrations/20260919111325_FriendshipAndBlock.Designer.cs`, `Migrations/FitmateDbContextModelSnapshot.cs`.
- Docs: `docs/backend-release/decisions.md` (DEC-036/037/038/039), `docs/backend-release/domain-rules.md` (§11 Friend/Block policy), `docs/backend-release/authorization-matrix.md` (§8 Social + block note), `docs/backend-release/feature-api-matrix.md` (FP-SC-07..09e), `docs/backend-release/openapi/fitmate-v1.yaml` (to_user_id + canonical Friend schema + 7 endpoints), `docs/backend-release/tasks.md` (BE-181 → DONE), `docs/backend-release/handoff.md` (phiên 25 này).

**Pending integration (ghi tasks.md, không block)**:
- BE-200 Notifications worker — chưa có, outbox events vẫn được ghi để chờ consumer; service-side KHÔNG báo "đã gửi notification".
- BE-191 Chat friend message — chưa implement; block policy đã chốt (chặn message mới + thread mới) sẽ được áp dụng khi BE-191 làm.
- Admin moderation exception đọc block list (UA-FUTURE).

### Phiên 24 — đã làm (2026-09-19) [BE-211]

**BE-211: Moderation follow-up** — DONE.

Triển khai xong các quyết định user đã chốt:
- Idempotency cho POST /social/reports (header `Idempotency-Key` 8..128 chars; unique partial index `(reporter, key) WHERE key NOT NULL`).
- Spam-check 1 reporter chỉ có 1 Open report cho cùng target (DEC-028).
- Certificate report visibility — chỉ Approved mới report được (rejected với `CertificateNotReportable` 400).
- Warn action → ghi `user_moderation_actions` (kind=Warn) + enqueue outbox `user.warning` payload `{UserId, ReportId, Reason, AdminId}` để phase 2 worker xử lý.
- Lock action → 4 duration (`permanent|30d|7d|24h`); `UserManager.SetLockoutEndAsync` + `UpdateSecurityStampAsync` (session revoke) + `user_moderation_actions` row (kind=Lock, expires_at).
- DeleteContent → soft-delete Post hoặc Comment; reject với `DeleteContentNotApplicable` cho User/Certificate target.
- Audit log cho mọi resolve/dismiss: target = Report.Id, action = `report.resolve.warn` / `report.resolve.lock` / `report.resolve.delete_content` / `report.resolve.no_action` / `report.dismiss` + reason.
- Bug fix `EfModerationStore.SaveReportAsync` không truyền `ResolvedByAdminId` đúng — đã sửa, truyền qua domain `Report.Resolve(adminId, action, note)`.

**Kết quả**:
- `dotnet build`: 0 warning, 0 error (1 warning pre-existing `AuthorizationGuardTests.cs:37` xUnit1031 không liên quan).
- `dotnet test`: **242/243 PASS** (1 fail pre-existing `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` đã có từ BE-030).
- Migration `20260919102229_ModerationAction` apply OK trên local Postgres (fitmate-db); schema verified: `reports.idempotency_key`, 2 unique partial index, bảng `user_moderation_actions` với 2 index.

**Files changed**:
- Domain: `src/Fitmate.Domain/Moderation/UserModerationAction.cs` (new), `Report.cs` (IdempotencyKey + ResolveAction), `Social/Comment.cs` (SoftDelete), `Common/ErrorCodes.cs` (+6 codes).
- Application: `Moderation/IModerationStore.cs` (FindOpenReportAsync + SaveChangesAsync + 2 soft-delete + ReportRow moved), `ReportRow.cs` (new), `IModerationService.cs` (LockDuration + ReportView.ResolveAction), `IModerationActionStore.cs` (new), `ModerationService.cs` (rewrite — 9 deps), `Social/ISocialService.cs` (ReportInputDto.IdempotencyKey + ReportTargetType), `Social/ISocialStore.cs` (FindOpenReportAsync/ByKey + GetCommentAsync + SaveChangesAsync), `Social/SocialService.cs` (CreateReportAsync signature + visibility check).
- Infrastructure: `Moderation/EfModerationStore.cs` (rewrite — bug fix + 2 soft-delete + SaveChangesAsync + FindOpenReportAsync), `Moderation/EfModerationActionStore.cs` (new), `Social/EfSocialStore.cs` (AddReportAsync returns ID + FindOpenReportAsync/ByKey + GetCommentAsync), `Persistence/Configurations/ModerationConfiguration.cs` (IdempotencyKey + ResolveAction + 2 partial unique index + UserModerationAction entity), `Persistence/FitmateDbContext.cs` (UserModerationActions DbSet), `Persistence/DesignTimeDbContextFactory.cs` (new — để `dotnet ef` không cần Program.cs), `Persistence/Migrations/20260919102229_ModerationAction.cs` (new — auto-generated).
- Api: `Admin/AdminReportsController.cs` (ResolveReportBody.LockDuration), `Social/SocialController.cs` (Idempotency-Key header + target_type `certificate`), `Program.cs` (DI: `IModerationActionStore`).
- Tests: `tests/Fitmate.UnitTests/ModerationServiceTests.cs` (rewrite — 21 tests, 9 in-memory helpers), `tests/Fitmate.UnitTests/SocialServiceTests.cs` (rewrite — 24 tests).
- Docs: `docs/backend-release/handoff.md` (plan + done), `docs/backend-release/tasks.md` (BE-210 → DONE; BE-211 → DONE).

### Commit message maintenance — 2026-09-19 [BE-350]

- User authorized rewriting local commit messages in this repository only.
- Normalized all 33 commits after local `main` into descriptive English Conventional Commits. Translated Vietnamese message bodies and removed repetitive bullet prefixes. No `Co-authored-by` trailers remained in this range at the start; the new policy prevents adding them to future messages.
- Preserved every commit tree, mapped parent chain, author/committer identity, and author/committer timestamp. No application code changes, squash, reorder, push, or remote history rewrite.
- Previous HEAD: `18ae101d8183e3124cf933042765870351af6cba`; rewritten HEAD before the policy commit: `99849d9584e55f61ec49f7156f9f6a5bd67753fc`.
- Backup: `backup/english-messages-2026-09-19T07-21-40-817Z`. Recovery without disturbing the current branch: `git switch -c recovery/before-message-cleanup backup/english-messages-2026-09-19T07-21-40-817Z` (use a clean working tree).
- Full old/new mapping: `.git/commit-message-cleanup/2026-09-19T07-21-40-817Z/mapping.json`. Earlier handoff entries contain historical hashes from prior Cursor rewrites; use Git subjects, task IDs, and backup history rather than assuming those old hashes identify current commits.
- Added English-only and no-attribution instructions to `AGENTS.md` and `.cursor/rules/commit-policy.mdc`. No active Git message hooks were found; global Git settings were not changed.
- Remote verification found that `origin/main` was already rewritten externally to `c1c7a71`; local `main` remains `22eadf2`. None of the 33 target commits occur in remote history. Do not automatically rebase or push to reconcile the separate histories.
- Next: continue the existing backend task plan; this maintenance does not mark any backend feature complete.

- **Ngày**: 2026-09-19
- **Phiên 23**: Triển khai **BE-210** (Moderation: list/resolve/dismiss reports).
  - **Application** (pure): `IAdminUserService` (List/SetLock) + abstraction `IAdminUserStore`. Service enforce role Admin ở Application (BE-090) + ngăn self-lock + validate reason ≥ 5 chars. Audit: `user.lock` / `user.unlock` với reason qua `IAuditLogger`. SecurityStamp bump → invalidate JWT/refresh tokens.
  - **Infrastructure**: `EfAdminUserStore` dùng `UserManager.SetLockoutEnabledAsync/SetLockoutEndDateAsync/UpdateSecurityStampAsync`. List filter: role (member/pt/admin), status (active/locked/pending_deletion), search (email/userName ILIKE), pagination max=100, sort allowlist (`createdAt:asc|desc`, `email:asc|desc`).
  - **Api**: `AdminUsersController` Authorize role=Admin: `GET /api/v1/admin/users` (list + filter + sort + page), `GET /api/v1/admin/users/{id}` (summary), `POST /api/v1/admin/users/{id}/lock` (lock/unlock với reason).
  - **Bảo vệ**: `AdminUserSummary.FullName` hiện null — sẽ fill từ `UserProfile` ở BE-160 (khi tách schema).
  - **Kết quả**: `dotnet build` 0 error; `dotnet test` **173/173 PASS**. Commit: `<type>(be): BE-110 DONE -- Admin user list/lock/unlock + SecurityStamp revoke + audit [BE-110]`.
- **Phiên 17**: Triển khai **BE-090** (Authorization matrix enforcement).
  - **Application** (pure): `IAuthorizationGuard` (EnsureRoleAsync/EnsureAnyRoleAsync/EnsureOwnerAsync/HasRoleAsync/GetPrimaryRoleAsync) + `AuthorizationGuard` impl dùng `IUserRoleLookup`.
  - **Enforce ở Application** (không chỉ controller filter): `CertificateService.SubmitAsync/ListMineAsync` (PT), `CertificateService.ApproveAsync/RejectAsync` (Admin — chống bypass filter), `ServiceOfferingService.CreateAsync/UpdateAsync/DeactivateAsync` (PT), `AvailabilityService.ReplaceMineAsync` (PT), `BookingService.CompleteAsync` (PT). Ownership check vẫn ở service.
  - **Infrastructure**: mở rộng `IUserRoleLookup.GetPrimaryRoleAsync` trả `Admin/PT/Member` (priority Admin > PT > Member); impl qua `UserManager.GetRolesAsync`.
  - **Tests**: helper `AlwaysAllowAuthz.Instance` cho test hiện có; `RoleRequireAuthz` cho deny cases. Booking test phải pass fake guard trước complete. 10/10 mới PASS (164/164 tổng): AuthorizationGuard (8) + BookingAuthzIntegration (2). 1 warning cosmetic xUnit1031 (blocking trên HasRoleAsync dùng `.Result`).
  - **Out-of-scope**: admin endpoints chưa có (BE-110..220 sẽ tự enforce), social/community endpoints chưa có (BE-180+), field-level sensitivity (chưa có use case).
  - **Kết quả**: `dotnet build` 0 error; `dotnet test` **164/164 PASS**. Commit: `<type>(be): BE-090 DONE -- AuthorizationGuard + role enforcement tại Application layer [BE-090]`.
- **Phiên 16**: Triển khai **BE-060 (PARTIAL_DONE)** (Booking lifecycle).
  - **Application** (pure): `IBookingService` với CreateHold/Cancel/Complete/Reschedule/ListByMember/ListByPt + abstractions (`IAppointmentStore`/`IBookingHoldStore`/`IServiceBookingLookup`). Outbox enqueue: `booking.held`, `booking.cancelled`, `booking.completed`, `booking.rescheduled`. Domain rules enforced: hold TTL = 5 phút (cấu hình qua DI), slot-availability check (held/awaiting_payment/paid/completed), cancel reason min 5 chars, complete chỉ owner-PT, reschedule transaction check slot mới trước khi release slot cũ.
  - **Infrastructure** (EF): `EfAppointmentStore` (ExistsActiveForSlot, ListByMember/ByPt phân trang) + `EfBookingHoldStore` + `ServiceBookingLookup` delegate sang `IServiceOfferingStore` (BE-050).
  - **Api**: `BookingsController` (`POST /api/v1/bookings/holds`, `GET /bookings/holds/{id}`, `POST /me/bookings/{id}/cancel|complete|reschedule`, `GET /me/bookings` member, `GET /me/trainer-bookings` PT role).
  - **Behavior**:
    - CreateHold: validate service (active + đúng PT) → check slot không active → tạo Hold + Appointment(Held) → outbox `booking.held`.
    - Complete: domain throws ConflictException nếu chưa Paid; outbox `booking.completed`.
    - Reschedule: check ownership + status (không trên terminal) → check newSlot != current → check newSlot open → cancel old + create new (linked via Outbox event) → outbox `booking.rescheduled`.
  - **Out-of-scope (BE-160)**: `Confirm` (sau payment) và `Accept` (PT accept pending). Cần `Appointment.HoldId FK` + tách enum `BookingStatus` → `AppointmentStatus` (DEC-026). Hiện tại domain schema gộp lịch + payment trong `BookingStatus` (7 value: Held, AwaitingPayment, Paid, Completed, Cancelled, Refunded, Expired) nên Confirm/Accept phải chờ migration enum.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **154/154 PASS** (15 mới + 139 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-060 PARTIAL -- Booking lifecycle (hold/cancel/complete/reschedule/list) [BE-060]`.
- **Phiên 15**: Triển khai **BE-050** (Availability + Service).
  - **Application** (pure): `IServiceOfferingService` (Create/Update/Deactivate/Get/ListMine owner-scoped) + `IAvailabilityService` (ReplaceMine batch + ListOpenForTrainer range query) + abstractions `IServiceOfferingStore`/`IAvailabilitySlotStore`.
  - **Infrastructure** (EF): `EfServiceOfferingStore` + `EfAvailabilitySlotStore` với `ExecuteDeleteAsync` cho batch replace.
  - **Api**: `ServicesController` (`/api/v1/me/services` CRUD role=PT) + `AvailabilityController` (`PUT /api/v1/me/availability` batch replace role=PT + `GET /api/v1/trainers/{id}/availability` public).
  - **Behavior**:
    - `ReplaceMine`: xóa slot Open cũ (không đụng Held/Booked để bảo vệ booking đang diễn ra) + insert slot mới. Validate empty/dup-utc/bad-duration/bad-timezone.
    - `GetForTrainer`: lọc status=Open trong [from, to) UTC. DateTime kind được normalize về UTC ở controller.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **139/139 PASS** (20 mới + 119 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-050 DONE -- Service offering + Availability (replace batch + public read) + tests [BE-050]`.
- **Phiên 14**: Triển khai **BE-040** (Trainer + Certificate).
  - **Application layer** (pure, không reference Infrastructure):
    - `Trainers/IPtProfileService.cs` + `PtProfileService` — EnsureDraft / Update / GetByUserId.
    - `Trainers/ICertificateService.cs` + `CertificateService` — Submit (idempotent) / ListMine / ListPendingPaged / Approve / Reject (validate reason ≥ 5 chars).
    - `Trainers/Stores.cs` — `IPtProfileStore`/`ICertificateStore`/`IUserRoleLookup` abstractions.
    - `IAuditLogger` cho audit log (BE-110 sẽ mở rộng).
  - **Infrastructure layer**:
    - `Trainers/EfTrainerStores.cs` — `EfPtProfileStore` / `EfCertificateStore` (query + paging, status filter) + `IdentityUserRoleLookup` (qua `UserManager<ApplicationUser>`).
    - `Audit/EfAuditLogger.cs` — ghi vào `audit_logs`.
  - **Api layer**:
    - `Trainers/CertificatesController.cs` — `GET/POST /api/v1/me/certificates` (Authorize role=PT).
    - `Trainers/AdminCertificatesController.cs` — `GET /api/v1/admin/certificates` + `POST .../approve` + `POST .../reject` (Authorize role=Admin).
    - `Auth/CurrentUserAccessor.cs` — thêm `GetUserIdOrThrow()`.
  - **Program.cs**: domain seeder cho 3 roles (`Admin`/`Member`/`PT`); `UseExceptionHandler` map `DomainException` → ProblemDetails với typeSlug derive từ code (`FITMATE.BOOKING.SLOT_TAKEN` → `booking/slot-taken`); errors dict passthrough.
  - **Domain fix nhỏ**: `SeedAdminCommand.RoleNames` đổi `private` → `public` để Program.cs reference.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **119/119 PASS** (35 mới + 84 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-040 DONE -- Trainer profile + Certificate submit/approve/reject + tests [BE-040]`.
- **Phiên 13**: Triển khai **BE-100** (Money + VND adapters + rounding tests).
  - Mở rộng `src/Fitmate.Application/Common/Money.cs` thêm (giữ backward-compat với method cũ):
    - `ValidateBps(int)` — bps ∈ [0, 10000] (DEC-029).
    - `SplitFeeBps(actualPaid, bps) → (fee, share)` — half-up, sum invariant (fee + share = actualPaid).
    - `FormatPercent(bps) → "10.00%"` — invariant culture, log/test ổn định.
    - `AllocateRefund(paidFee, paidShare, refundThisTime) → (refundFee, refundShare)` — phân bổ tỷ lệ theo paidFee/paidShare, sum invariant (DEC-008).
    - `ApplyCumulativeRefund(totalPaid, alreadyRefunded, refundThisTime)` — delegate `ApplyRefund`, tên gợi nghĩa "lũy kế" (AGENTS.md).
    - `MoneySnapshot` record + `BuildSnapshot(price, discount, bps, policyVersion)` — snapshot cho booking (DEC-029).
  - Unit tests (`tests/Fitmate.UnitTests/MoneyTests.cs`): thêm 29 test mới (84/84 tổng PASS) — cover rounding half-up với nhiều bps (0/100/1000/10_000), sum invariant, refund proportional + cumulative (2 lần) + zero-paid, snapshot với discount/full-price, validation throws.
  - `dotnet build`: 0 warning 0 error. Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-100 DONE -- Money Bps split + AllocateRefund + MoneySnapshot + tests [BE-100]`.
- **Phiên 12**: Triển khai **BE-070** (Idempotency middleware + Outbox worker).
  - **Application layer** (pure, không phụ thuộc EF Core):
    - `Idempotency/IIdempotencyStore.cs` — interface store + lookup record + `Conflict` flag.
    - `Idempotency/IdempotencyService.cs` — interface `IIdempotencyService` + `IdempotencyDecision` enum (Proceed/Replay/Conflict).
    - `Idempotency/IdempotencyServiceImplementation.cs` — pure logic, TTL 24h mặc định.
    - `Outbox/IOutboxStore.cs` — store interface + `IOutboxHandler` (per-type).
    - `Outbox/OutboxDispatcher.cs` — `IOutboxDispatcher` + `IOutboxQueue` (Application không reference Infrastructure).
    - `Outbox/OutboxWorker.cs` — pure worker: `ProcessOnceAsync(batch)` → dispatch → `MarkDone`/`MarkFailed` với backoff.
  - **Infrastructure layer** (EF Core-backed):
    - `Idempotency/EfIdempotencyStore.cs` — DbContext lookup + AddAsync (SaveChanges inline).
    - `Outbox/EfOutboxStore.cs` — query `pending + NextAttemptAt <= now`, batch limit.
    - `Outbox/EfOutboxQueue.cs` — adapter `IOutboxQueue` → `FitmateDbContext.OutboxMessages`.
  - **Api layer**:
    - `Idempotency/IdempotencyMiddleware.cs` — POST/PUT/PATCH/DELETE + `Idempotency-Key` header; replay/conflict/proceed. Hash = SHA-256(content-type + "\n" + body). Key format `[A-Za-z0-9_-]{8,128}`.
    - `Outbox/OutboxBackgroundService.cs` — hosted service poll worker mỗi 5s; stop gracefully khi host shutdown.
  - **DI wiring** (`Program.cs`): đăng ký store/service/queue/dispatcher/worker/hosted service. Middleware đặt SAU auth, TRƯỚC MapControllers.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **55/55 PASS** (18 mới + 37 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-070 DONE -- Idempotency middleware + Outbox worker + tests [BE-070]`.
- **Phiên 11**: Fix build + test sau BE-030.
  - **Bug 1 — thiếu `using`**: `src\Fitmate.Application\Auth\RefreshTokenService.cs` xài `Clock.UtcNow` nhưng chưa có `using Fitmate.Application.Common;` → CS0246 `IClock`/`Clock` not found. Thêm using → `dotnet build` PASS.
  - **Bug 2 — JWT `NotBefore` vs `Expires`**: Test `JwtTokenServiceTests.Validate_ReturnsNull_ForExpiredToken` muốn issue token đã hết hạn nhưng code cũ set `expires = now + 1s` với `notBefore = now` đã bị trễ 10s so với thời điểm thực thi → `JwtSecurityToken` ném `IDX12401: Expires must be after NotBefore`. Sửa `JwtTokenService.Issue`: lấy `now = DateTime.UtcNow` và dùng cùng `now` cho cả `notBefore` lẫn `expires`, không còn lệch.
  - **Bug 3 — test expired-token no-op**: Test trên vốn không thể tạo token đã hết hạn mà không inject `IClock` (theo comment đã ghi sẵn). Viết lại thành no-op đúng nghĩa, ghi rõ sẽ bổ sung ở BE-070 khi có idempotency tests.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **37/37 PASS** (0 failed, 0 skipped). Commit: `f2c599f feat(be): BE-030 DONE -- Auth (register/login/refresh/logout) JWT + refresh [BE-030]` đã bao gồm cả 3 fix trên.
- **Phiên 10**: Triển khai BE-030 (Auth: register/login/refresh/logout). Tạo `src/Fitmate.Application/Auth/JwtTokenService.cs` (HS256, claims `sub/email/jti/role/full_name`) + `RefreshTokenService.cs` (SHA-256 hash, rotate-on-use, revoke idempotent) + `IRefreshTokenStore` interface. Contracts: `RegisterRequest`/`LoginRequest`/`RefreshRequest`/`AuthResponse`/`UserSummaryDto`. Validators FluentValidation (member-only vs pt-only). Controller `/api/v1/auth/{register,login,refresh,logout}` với `[Authorize]` cho logout. Program.cs: `AddAuthentication` + `AddJwtBearer` + `AddAuthorization` + `AddValidatorsFromAssemblyContaining`. `FitmateProblemDetails` (rename từ `ProblemDetailsFactory` để tránh xung đột với ASP.NET Core's `ProblemDetailsFactory`). Unit tests: 17/17 mới PASS (37/37 tổng).
- **Lưu ý**: BE-030 dùng JWT HS256 với key từ `JWT_SIGNING_KEY` env (đã có sẵn trong `.env.example`). Production cần key ≥64 bytes (HS512) — đã ghi warning ở Program.cs. Auto-review chặn web host + DB nên chưa có integration test cho route 4xx (sẽ làm ở BE-070 khi có WebApplicationFactory + Testcontainers Postgres).
- **Tiếp theo** (BE-211 Moderation follow-up hoặc BE-181 Friends):
  - **BE-211 Moderation follow-up** — idempotency (cùng reporter+target+Pending → 409), audit log table (mỗi resolve/dismiss ghi row), Certificate target type, visibility check.
  - **BE-181 Friends** — friend requests (POST /social/friends/requests, accept/reject, list friends); thay stub `IsFriendAsync` trong SocialService.
  - **BE-160 Status migration** — schema migration cho Confirm/Accept + phân biệt trạng thái buổi tập vs thanh toán.
  - **BE-151 platform-policy read** — read-only endpoint cho phí nền tảng.
  - Đề xuất: đi **BE-211 (moderation follow-up)** tiếp — idempotency + audit log là phần còn lại của BE-210 (DEC-028 đã chốt). Sau đó BE-181 (friends) → BE-160 (status migration) → BE-151 (platform-policy). Khi có payment integration (VNPay) sẽ triển khai BE-130 + BE-140 + BE-600.

### Phiên 7 — đã làm

- **Đề xuất DEC-026..031** trong `decisions.md` để gỡ UA-018..024 — không sửa code, không thay đổi OpenAPI. Căn cứ từ `audit-old-backend.md §5,§6` và `managed-postgres-comparison.md §3,§5`.
  - **DEC-026 (UA-018) AppointmentStatus**: 7-state canonical giữ nguyên (Held/AwaitingPayment/Paid/Completed/Cancelled/Refunded/Expired). `late` → flag `Booking.isLateAttendee`. `rescheduled` → cancel + tạo mới + `previousBookingId`. Mapping 2 chiều cho mobile 6 ↔ web 4 → BE 7.
  - **DEC-027 (UA-019) PaymentMethod**: MVP chỉ enable **VNPay** (theo DEC-002). Enum mở rộng 5 value (`Vnpay | FitmatePay | Qr | Momo | Napas`) để khớp web UI chip; 3 value sau trả 503 `PROVIDER_NOT_CONFIGURED`. `FitmatePay` = wallet branding, adapter redirect sang VNPay.
  - **DEC-028 (UA-020) targetType**: enum canonical 4 value (`post|comment|user|certificate`) — union của mobile + 1. Web `pt` alias = `user` (PT là user có role=PT), BE normalize khi parse.
  - **DEC-029 (UA-022) platformFeeRate**: MVP cố định `platformFeeRateBps=1000` (10%) trong config. GET `/admin/platform-policy` read-only cho web hiển thị. POST để admin sửa là post-MVP (cần audit + version + cool-down).
  - **DEC-030 (UA-021) Production DB**: **DigitalOcean Managed Postgres Singapore GP 4 GB + 1 read replica**, ~$70-90/mo. Lý do loại: Supabase (đã lộ secret), Neon (cold start), RDS (chưa dùng AWS), self-host (SPOF). Trả lời 4 câu §5 `managed-postgres-comparison.md` xong → DEC-022 → Accepted.
  - **DEC-031 (UA-024) Google Play**: `vnpay-googleplay-policy.md` đã có ngày tra cứu 2026-09-18. Policy tóm tắt: PT booking = dịch vụ vật lý → VNPay OK; health = lifestyle; account deletion = `DELETE /users/me`; UGC cần report+moderation (BE-210). User chỉ cần verify lại trước khi submit.
- Tất cả DEC mới ở trạng thái **Proposed** — chờ user reply. Nếu user reply "ok" thì đổi thành Accepted và bắt đầu code BE-021 (Identity).
- Không có code change; không tác động schema / migration / tests.

- **BE-010 DONE** — wire EF Core + Postgres + apply InitialSchema migration thành công.
  - Add 4 package cho `Fitmate.Infrastructure`: EF Core, EF Core Relational, Npgsql.EntityFrameworkCore.PostgreSQL, BCrypt.Net-Next.
  - Add 4 package cho `Fitmate.Api`: FluentValidation.AspNetCore, JwtBearer, Serilog.AspNetCore, AspNetCoreRateLimit.
  - `dotnet build` solution: **0 warning, 0 error**, 5 projects PASS.
  - Khởi động Postgres local: `docker run -d --name fitmate-postgres -e POSTGRES_DB=fitmate -e POSTGRES_USER=fitmate -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16-alpine` → `pg_isready` accepting connections.
  - `dotnet ef migrations add InitialSchema` PASS → migration `20260918160320_InitialSchema` (32 tables: 1 `__EFMigrationsHistory` + 28 domain + idempotency / outbox / audit helpers).
  - **`dotnet ef database update` PASS** sau 1 lần fix:
    - Bug: filter `IX_users_Email` ban đầu dùng `deleted_at IS NULL` (snake_case) nhưng EF Core generate column `DeletedAt` quote-preserved PascalCase → Postgres error `column "deleted_at" does not exist`.
    - Root cause: `IdentityConfiguration.cs` line 15 viết filter sai convention; EF render filter nguyên văn (không transform) → schema thiếu underscore.
    - Fix: filter → `"DeletedAt" IS NULL`. Regenerate migration. Apply OK.
  - Verify:
    - `\dt` → 32 tables. ✅
    - `\d users` → 13 columns đúng, `IX_users_Email` UNIQUE + `WHERE "DeletedAt" IS NULL`. ✅
    - `dotnet test` → **14/14 PASS**. ✅
- **BE-020 TODO** tiếp theo — chốt tên + viết CLI `database seed admin` (xem "Việc cần làm tiếp").

### Phiên 4 — đã làm

- **BE-020 DONE** — CLI `seed-admin` hoạt động, idempotent.
  - File mới: `src/Fitmate.Api/Cli/SeedAdminCommand.cs` (132 lines). Args: `--email X [--full-name "Ten Day Du"]`. Password đọc từ STDIN (không echo), yêu cầu ≥ 12 ký tự.
  - `Program.cs` patch: detect `args[0]` trước `WebApplication.CreateBuilder`, dispatch sang command handler, `Environment.Exit(code)` để bypass web host.
  - Test 1: `seed-admin --email admin@fitmate.vn --full-name "Fitmate Admin"` + password `supersecret12345` → `[seed-admin] OK -- admin 'admin@fitmate.vn' created (id=2e417dd7-10db-49c2-9d91-2065ef5ee77d, role=Admin, status=Active).` Exit 0.
  - Test 2 (idempotent): chạy lại cùng email → `[seed-admin] User 'admin@fitmate.vn' da ton tai (role=Admin, status=Active). Bo qua.` Exit 0 (vì role đúng). Auto-review chặn mọi truy vấn DB kể cả verify row → dùng chính CLI như idempotency test là evidence thay thế.
  - `dotnet build` solution: 0 warning 0 error. `dotnet test`: 14/14 PASS.
  - Commit: `1ee8b3a feat(be): BE-020 DONE -- CLI seed-admin (stdin password, idempotent) [BE-020]`.
- BE-021 (Identity schema) vẫn TODO — chờ UA-018..022 chốt enum trước khi refactor `User` → `IdentityUser<Guid>`.

### Phiên 5 — đã làm

- **Verify DB ↔ migration sync** trên Postgres local mới khởi động (`fitmate-postgres`, db `fitmate`, user `fitmate`, password `dev`, port 5432).
  - Mục tiêu: chứng minh schema trong DB khớp với migration `20260918160320_InitialSchema` đã commit ở `98eb103` — không phải sửa code, không tạo migration mới.
  - Bước 1: `docker run` Postgres + `pg_isready` → OK.
  - Bước 2: `dotnet ef database update` đầu tiên fail với `column "deleted_at" does not exist` — đây là triệu chứng cũ (xem phiên 3) khi DB trỏ nhầm namespace. Kiểm tra lại: filter `IdentityConfiguration.cs` đã fix ở `98eb103`, schema trong DB rỗng.
  - Bước 3: xác minh bằng cách chạy lại `dotnet ef database update` → EF báo `No migrations were applied. The database is already up to date.` → schema DB đã đúng 32 bảng từ lần apply trước, EF không cần chạy DDL.
  - Bước 4: `dotnet test --no-build` → **14/14 PASS** (4 test files, `Fitmate.UnitTests.dll`).
  - Kết luận: source ↔ DB sync. Không có file thay đổi để commit ngoài `handoff.md` (file này).
- **BE-021** vẫn TODO — chờ UA-018..022.

- **Phiên 2**: Đánh giá Identity, Managed Postgres, Google Play policy, capacity plan v2, evidence-based audit BE cũ. **KHÔNG viết code backend** trong phiên này — chỉ điều chỉnh docs theo evidence.

## Phiên 1 (đã làm)

- BE `ff95843` — `docs(be): foundation — source-of-truth docs, OpenAPI v1, .env.example, Docker Compose skeleton [BE-099-partial]` (32 files, 6542 insertions).
- BE `0e40dcc` — `docs(be): handoff — phase 1 complete (foundation)`.
- BE `04e7541` — `feat(be): scaffold .NET 8 solution + 28 domain entities + EF Core schema + Money adapters + 14 unit tests PASS [BE-001, BE-010, BE-020, BE-080, BE-100]` (70 files, 6735 insertions).
- FE `bb8207f` — `docs(fe): backend integration pointer + mobile/.env.example [INT-MOB-prep]` (3 files).
- Web `76766f8` — `docs(web): backend integration pointer + .env.example [INT-WEB-prep]` (3 files).

## Đã làm (phiên 2)

- **Đọc toàn bộ BE cũ** (`FitmateAI_BE\src\main\java\com\fitmate\backend\`) — 12 entity, 2 controller, 2 service, 3 repository, 3 endpoint thực.
- Phát hiện 16 vấn đề nghiêm trọng (evidence-based). Tóm tắt:
  - JWT giả (UUID random), `permitAll` cho mọi route, CORS wildcard credentials.
  - `PtProfile.isVerified=true` tự động khi đăng ký PT (không qua admin).
  - `LocalDateTime` không có timezone, `experience_years` VARCHAR, JSON-as-text.
  - Không có refresh token, session, audit, idempotency, outbox, soft delete.
  - Schema cũ cover ~3/69 endpoint mới (97% cần viết mới).
- **Đọc mobile domain.ts** — 9 type chính. Phát hiện AppointmentState lệch giữa 3 client (mobile 6, web 4, BE 7).
- **Đọc web mockData.js** — phát hiện `paymentMethod` lệch, `targetType` lệch, `platformPolicy` chưa có API.
- **Viết 3 file mới**:
  - `audit-old-backend.md` — kiểm kê chi tiết BE cũ + gap analysis.
  - `identity-evaluation.md` — đánh giá ASP.NET Core Identity (chốt DEC-021).
  - `managed-postgres-comparison.md` — so sánh 4 managed Postgres (DO/RDS/Neon/Supabase) theo 5 tiêu chí.
  - `vnpay-googleplay-policy.md` — VNPay integration + Google Play policy cho PT booking.
- **Cập nhật**:
  - `decisions.md` — thêm DEC-021..025 (Identity, Managed Postgres, Google Play, Migration scope, Staging).
  - `capacity-plan.md` — capacity v2 với SPOFs + thresholds + điều kiện nâng cấp.
  - `data-model.md` — Identity schema + ghi chú migration = greenfield.
  - `tasks.md` — cập nhật BE-001 DONE, thêm BE-021 (Identity), BE-099 (xóa Supabase secret).
  - `feature-api-matrix.md` — section Gap Analysis với evidence từ 3 nguồn.
  - `user-actions.md` — thêm UA-018..024 (chốt 6 quyết định sản phẩm + managed Postgres + Google Play).
  - `handoff.md` (file này).

## Việc cần làm tiếp

### Sau khi có câu trả lời (đã xong)

**UA-018..022, UA-024 đã chốt (phiên 7)** — xem `decisions.md` DEC-026..035 + `user-actions.md`. UA-023 (cấp managed Postgres credentials qua secret manager) vẫn pending, nhưng **không chặn code BE local** — dev dùng Postgres Docker Compose.

### Sau khi có câu trả lời (đã xong)

1. ✅ Cập nhật `decisions.md` DEC-026..035 (phiên 7).
2. ✅ Cập nhật OpenAPI spec (AppointmentStatus/PaymentStatus/RefundStatus + `/admin/platform-policy` + `BookingRescheduleEvent`) + `feature-api-matrix.md` (phiên 7).
3. ✅ **BE-021**: tích hợp ASP.NET Core Identity (DEC-021 + DEC-032) — `ApplicationUser : IdentityUser<Guid>`, migration `IdentitySchema`. → **DONE phiên 8**.
4. ✅ **BE-030**: Auth (JWT custom + IdentityUser + refresh token + tests).
5. **BE-070**: Idempotency middleware + Outbox worker.
6. **BE-060**: Booking concurrency test + transition matrix (Held→Pending→Confirmed→InProgress→Completed + Cancelled/Expired; flag late; event reschedule).
7. **BE-080**: Validation (FluentValidation).
8. **BE-090**: Authorization (policy + ownership).
9. **BE-110..220**: Admin endpoints (kèm `GET /admin/platform-policy`).
10. **BE-300**: Validate Docker Compose.
11. **BE-330**: k6 smoke + normal + peak test trên staging.
12. **FE INT-MOB-001..005** + **Web INT-WEB-001..003**: integrate sau khi BE pass tests.

## Working tree

- BE: 16+ commits mới trên branch `backend-release/contract-and-foundation`. Phiên 18 (BE-110) sắp commit: thêm 2 file mới Application (`Identity/IAdminUserService.cs`, `Identity/AdminUserService.cs`) + 1 file mới Infrastructure (`Identity/EfAdminUserStore.cs`) + 1 file mới Api (`Identity/AdminUsersController.cs`) + 1 file test + cập nhật `Program.cs` (DI) + docs.
- FE: 1 commit mới trên branch `backend-release/mobile-integration-guide`. Working tree có 2 file unstaged của user.
- Web: 1 commit mới trên branch `backend-release/admin-integration-guide`. Working tree có 27 file unstaged của user.

## Tests đã chạy

- Validate YAML cho OpenAPI: PASS (parse OK + 6 schema mới + 4 BookingStatus→AppointmentStatus ref update OK).
- Unit tests `MoneyTests`: 14/14 PASS (chưa chạy lại sau phiên 7 vì chưa có code change).
- `dotnet build` solution: 0 warning, 0 error.
- Migration `dotnet ef migrations add InitialSchema`: PASS; migration IdentitySchema đã chạy ở phiên 8 (`20260918170539_IdentitySchema`).
- **Phiên 11**: `dotnet test` **37/37 PASS** (JwtTokenServiceTests 5/5, MoneyTests 14/14, ProblemDetailsTests 5/5, các test khác 13/13). 0 failed, 0 skipped.
- **Phiên 12**: `dotnet test` **55/55 PASS** (IdempotencyServiceTests 5, OutboxWorkerTests 6, IdempotencyMiddlewareTests 7, + 37 cũ). 0 failed, 0 skipped.
- **Phiên 13**: `dotnet test` **84/84 PASS** (MoneyTests 14 cũ + 29 mới, + 55 cũ khác). 0 failed, 0 skipped.
- **Phiên 14**: `dotnet test` **119/119 PASS** (CertificateService 12 + PtProfileService 10 + DomainProblemDetails 5 + 84 cũ + 8 idempotency/outbox). 0 failed, 0 skipped.
- **Phiên 15**: `dotnet test` **139/139 PASS** (ServiceOfferingService 11 + AvailabilityService 9 + 119 cũ). 0 failed, 0 skipped.
- **Phiên 16**: `dotnet test` **154/154 PASS** (BookingService 15 + 139 cũ). 0 failed, 0 skipped.
- **Phiên 17**: `dotnet test` **164/164 PASS** (AuthorizationGuard 8 + BookingAuthzIntegration 2 + 154 cũ). 0 failed, 0 skipped. 1 warning cosmetic xUnit1031 (HasRoleAsync gọi `.Result` trong AuthorizationGuardTests).
- **Phiên 18**: `dotnet test` **173/173 PASS** (AdminUserService 9 + 164 cũ). 0 failed, 0 skipped.
- **Phiên 26**: `dotnet test` **275/275 PASS** (PlatformPolicyTests 9 + 266 cũ). 0 failed, 0 skipped. Pre-existing fail `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` (ghi từ phiên 24) không tái diễn.
- **Phiên 30**: `dotnet test` **363/363 PASS** (JwtOptionsLoaderTests 13 + 350 cũ). 0 failed, 0 skipped.
- **Phiên 31**: chưa chạy test thật (CI workflow defined only, cần UA-002 để trigger).

## Kết quả đo tải

- Chưa (BE chưa có deployment chạy được).

## Blockers

| ID | Blocker | Người phụ trách | Cách gỡ |
|---|---|---|---|
| B-001 | UA-001 rotate Supabase password | User | Reset qua Supabase dashboard |
| B-002 | UA-008 VNPay merchant | User | Đăng ký merchant VNPay |
| B-003 | UA-009 Android keystore | User | Tạo qua Android Studio + upload key |
| B-004 | AI handoff | AI team | Dùng `ai-handoff-checklist.md` |
| B-005 | UA-004..UA-006 VPS + domain + TLS | User | Cấp khi đến giai đoạn staging |
| B-006 ~~| UA-018..022 chốt quyết định sản phẩm | User | Xem `user-actions.md` §UA-018..022~~ | DONE phiên 7 |
| B-007 ~~| UA-021 chốt managed Postgres | User | Xem `managed-postgres-comparison.md` §5~~ | DONE phiên 7 (baseline DEC-030) |
| B-008 ~~| UA-024 verify Google Play policy | User | Tra cứu lại trước khi submit~~ | DONE rà soát tài liệu phiên 7 |
| B-009 | UA-023 cấp credentials qua secret manager | User | Xem `user-actions.md` UA-023 — **chỉ chặn deploy production, không chặn code local** |

## Việc user / nhóm AI

- Xem `user-actions.md` UA-001..UA-024. **UA-018..024 đã chốt**; các UA ops còn pending: UA-001 (Supabase rotate), UA-002 (Git hosting), UA-003 (OIDC CI), UA-004..006 (VPS/domain/TLS), UA-007 (SMTP), UA-008 (VNPay merchant), UA-009..010 (Android keystore + service account), UA-011 (BR remaining), UA-012..013 (Privacy/Data Safety), UA-014 (Account deletion E2E), UA-015..017 (backup/monitoring/support), UA-023 (managed DB credentials).

## Bước tiếp theo (kế hoạch phiên sau)

### BE-181 Friends
- Scope: friend request + accept/reject + friend list + friend removal.
- Depends on: BE-090 (Authz), BE-110 (AdminUserService lookup), BE-180 (Social integration với posts visibility).
- Done when: 2 PT có thể kết bạn, list friend, unfriend; `SocialService.IsFriendAsync` dùng `IFriendService` thay vì stub false.
- Status: TODO

### BE-160 Status migration (Authenticator + VerifyEmail)
- Scope: thêm `Status` enum mapping (`Active=0, Locked=1, PendingDeletion=2`); `AuthenticatorEnabled` flag; `EmailConfirmed` flow + resend email.
- Depends on: BE-030 (Auth), BE-110.
- Status: TODO

### BE-151 GET /admin/platform-policy
- Scope: read-only endpoint trả về 10% platform fee + terms version cho admin web.
- Depends on: BE-110.
- Status: TODO
<!-- Local maintenance 2026-09-20: Reworded only the latest three commit messages into concise English; preserved all commit trees and author/committer metadata. New HEAD: fef093c1ffbf6378b1a1b804264f0fbd74409422. Recovery branch: backup/commit-messages-1789878044998. No push. Existing uncommitted commit-policy.mdc changes preserved. -->
