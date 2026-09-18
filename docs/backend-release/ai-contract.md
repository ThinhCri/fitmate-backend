# AI Contract — Đề xuất (chờ nhóm AI bàn giao)

> **Quan trọng**: File này là đề xuất phía backend. Nhóm AI chưa xác nhận. Mọi chi tiết runtime/artifact phải được bàn giao qua checklist ở `ai-handoff-checklist.md`. Backend sẽ không tự chọn provider, tự train, hay tự giả định GPU.

## 1. Capability chính cần AI

| Capability | Endpoint gọi | Input | Output |
|---|---|---|---|
| Meal image scan | `POST /me/nutrition/scan` | image (multipart) | `{ items: [{name, calories, protein_g, carbs_g, fat_g}], total_calories, confidence, source: "ai_estimate", note }` |
| AI companion chat | `POST /chat/ai/messages` | `{ text, thread_id? }` | `{ text, thread_id, confidence }` |
| Adaptive workout scoring (FP-018) | `POST /me/workout/sessions` adapter (chưa rõ) | session + tags | `{ difficulty_adjustment, next_session_hint }` (chưa rõ scope) |

Còn lại (chưa cần AI): booking, payment, moderation, social graph.

## 2. Quy ước input/output

### 2.1 Meal scan

- Input:
  - `multipart/form-data` với `file` (jpeg/png/webp ≤ 5MB).
  - Optional `portion_hint: small|medium|large`.
- Output (HTTP 200):
  ```json
  {
    "items": [
      {"name": "Cơm trắng", "calories": 200, "protein_g": 4, "carbs_g": 45, "fat_g": 0.5}
    ],
    "total_calories": 450,
    "confidence": 0.83,
    "source": "ai_estimate",
    "note": "Ước tính dựa trên ảnh. Có thể điều chỉnh trước khi lưu."
  }
  ```
- Output KHÔNG ghi DB. User xác nhận mới lưu qua `POST /me/nutrition/meals`.

### 2.2 Chat AI

- Input:
  ```json
  {"text": "Tôi nên ăn gì sau khi tập?", "thread_id": "optional"}
  ```
- Output:
  ```json
  {"text": "...", "thread_id": "...", "confidence": 0.7}
  ```
- Thread có thể có nhiều message; history lưu ở BE.

### 2.3 Validation chung

- Mọi output AI phải qua schema validation trước khi trả FE. Output không khớp schema → `FITMATE.AI.INVALID_OUTPUT` (502 cho client).
- Mọi output có `confidence ∈ [0,1]`. Nếu không có → reject.

## 3. Errors

| Code | HTTP | Ý nghĩa | Hành động FE |
|---|---|---|---|
| `FITMATE.AI.UNAVAILABLE` | 503 | Provider down / timeout | Fallback UI; KHÔNG dùng fake result. |
| `FITMATE.AI.INVALID_OUTPUT` | 502 | Output không khớp schema | Bug; báo user "tính năng đang bận". |
| `FITMATE.AI.RATE_LIMITED` | 429 | User spam | Toast + Retry-After. |
| `FITMATE.AI.QUOTA_EXCEEDED` | 402 | Token/credit hết | Disable feature + admin notice. |

## 4. Limits (đề xuất, cần chốt)

- Timeout: 8 giây cho meal scan, 15 giây cho chat.
- Max concurrent AI request / instance: 20 (tune theo tải thực).
- Rate limit / user: 30 chat messages / phút, 60 scans / giờ.
- Token budget: chưa có; chờ nhóm AI.

## 5. Async vs sync

- Meal scan: **sync**, trả kết quả trong 1 round-trip. Nếu timeout → fail với `AI_UNAVAILABLE`.
- Chat AI: **sync** cho MVP. Có thể đổi sang SSE/socket sau nếu cần streaming.
- Workout adaptive: **sync** cho MVP.

## 6. Versioning

- Capability có version: `ai.v1.meal_scan`, `ai.v1.chat`.
- Đổi schema → bump version + additive in `ai.v1.*` cho 6 tháng.

## 7. Quyền

- AI **không** quyết định:
  - Role, verified flag.
  - Giá, payment, refund.
  - Booking status.
- Mọi output chỉ là **suggestion**; user xác nhận mới persist.

## 8. Privacy & retention

- Input/output AI: KHÔNG log payload ở prod. Chỉ log `traceId`, `capability`, `latencyMs`, `result_code`.
- Lưu DB kết quả chỉ khi user xác nhận (meal log).
- Retention: theo account lifecycle.

## 9. Trong khi AI chưa bàn giao

- BE adapter trả `FITMATE.AI.UNAVAILABLE` cho mọi capability.
- FE render UX fallback (input tay cho meal scan, generic response cho chat).
- Không tính năng nào bị chặn vì thiếu AI.
