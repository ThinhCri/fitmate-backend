// ─── Fitmate k6 load test — shared constants ───────────────────────────────────
// Adjust these to match your local environment.
// Default points at the dotnet-watch dev container which listens on 5116.

export const BASE_URL = __ENV.BASE_URL ?? 'http://localhost:5116';
export const API_PREFIX = '/api/v1';

// Demo accounts (same as FitmateWebApp/mockData.js)
export const MEMBER_EMAIL = 'user@fitmate.vn';
export const MEMBER_PASSWORD = '123456';
export const PT_EMAIL = 'pt@fitmate.vn';
export const PT_PASSWORD = '123456';
export const ADMIN_EMAIL = 'admin@fitmate.vn';
export const ADMIN_PASSWORD = '123456';
