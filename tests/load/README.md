# ─── k6 load test configuration ───────────────────────────────────────────────
# Each profile references the same scripts; only duration and VU counts differ.
# Use k6's built-in tag filtering to run a specific subset:
#   k6 run scripts/normal.js --tag checklabel=admin

# Environment variables (can override at runtime):
#   BASE_URL           — API base URL (default: http://localhost:8080)
#   K6_CLOUD_TOKEN     — Cloud results upload (optional)

# ─── Smoke ────────────────────────────────────────────────────────────────────
# Purpose : Fast sanity check that the service is up.
# VU      : 1
# Duration: 30 s
# Pass criteria: 0% error rate, p(95) < 500 ms
# Run: k6 run scripts/smoke.js

# ─── Normal ───────────────────────────────────────────────────────────────────
# Purpose : Typical day traffic — user browses, views bookings, admin checks metrics.
# VU      : 5 → 10
# Duration: 5 min
# Pass criteria: <2% errors, p(95) < 800 ms, p(99) < 1500 ms
# Run: k6 run scripts/normal.js

# ─── Peak ─────────────────────────────────────────────────────────────────────
# Purpose : High traffic period (e.g., early morning slot booking rush).
# VU      : 25 → 50
# Duration: 5 min
# Pass criteria: <5% errors, p(95) < 1200 ms
# Run: k6 run scripts/peak.js

# ─── Spike ─────────────────────────────────────────────────────────────────────
# Purpose : Sudden surge (e.g., viral promotion or targeted attack).
# VU      : 5 → 100 in 30 s
# Duration: 3.5 min
# Pass criteria: <10% errors, p(95) < 3000 ms (graceful degradation OK)
# Run: k6 run scripts/spike.js

# ─── Soak ─────────────────────────────────────────────────────────────────────
# Purpose : Long-running stability — detect leaks, pool exhaustion.
# VU      : 15 sustained
# Duration: 15 min
# Pass criteria: <3% errors, p(95) < 1000 ms, no trend increase in duration
# Run: k6 run scripts/soak.js

# ─── Stress ────────────────────────────────────────────────────────────────────
# Purpose : Find the breaking point — observe degradation gracefully.
# VU      : 10 → 100 (gradual ramp)
# Duration: 6.5 min
# Pass criteria: <20% errors (observe degradation), p(95) < 5000 ms
# Run: k6 run scripts/stress.js

# ─── Running via Docker ────────────────────────────────────────────────────────
# The `compose.loadtest.yml` profile starts a k6 container pre-configured to
# run all scripts sequentially against the local stack:
#   docker compose -f compose.yml -f compose.loadtest.yml up k6
#
# To run a single script via Docker:
#   docker compose -f compose.yml -f compose.loadtest.yml run --rm \
#     -e BASE_URL=http://api:8080 \
#     k6 run /scripts/smoke.js
