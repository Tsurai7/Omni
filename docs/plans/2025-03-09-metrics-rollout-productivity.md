# Instrumentation, Success Metrics, and Phased Rollout

**Date:** 2025-03-09  
**Scope:** Client productivity UX improvements (Daily Pulse, Session score, insights, Account preferences).

---

## Success metrics

### Product

| Metric | Target | How to measure |
|--------|--------|----------------|
| D7 retention | Baseline + lift | Track day-7 return after first use. |
| Sessions per day | Increase | Count of focus sessions started per user per day. |
| Daily goal completion rate | Track | % of days where focus minutes ≥ goal (from Home pulse). |
| Streak continuation rate | Track | % of users who maintain a streak after first week. |
| Time to first action | &lt; 30 s | Time from app open to “Start focus” or equivalent CTA tap. |

### UX quality

| Check | Description |
|-------|-------------|
| 5-second test | User can answer “Am I on track today?” from Home (goal ring + streak). |
| Session completion | Reduce abandoned sessions (started but not stopped properly). |
| Post-session feedback | User sees score/summary after stopping a session. |

---

## Instrumentation requirements

### Events to track (client)

- **Navigation:** Screen view (Home, Session, Usage stats, Account).
- **CTA taps:** `next_best_action_tap`, `start_focus_tap` (Session), `recommendation_chip_tap` (Usage).
- **Session lifecycle:** `session_started`, `session_stopped`, `session_countdown_completed`.
- **Score view:** `post_session_sheet_viewed`, `post_session_done_tap`.
- **Preferences:** `daily_goal_changed`, `notification_intensity_changed`, `streak_visible_changed`.
- **Errors:** Sync failures, auth failures (existing where applicable).

### Implementation notes

- Prefer a single analytics abstraction (e.g. `IAnalyticsService`) with no-op default; plug in provider later.
- Do not log PII in event payloads; use anonymous or hashed user id if needed.
- Backend can add its own metrics (e.g. session count, focus minutes) from existing APIs.

---

## Phased rollout checkpoints

### Phase 1: Foundation (done in this implementation)

- [x] Design tokens and ProductivityCard.
- [x] Home: Daily Pulse, Now, Next best action.
- [x] Session: post-session score sheet; pre-session caption.
- [x] Usage: insight cards and recommendation chip.
- [x] Account: productivity preferences (goal, notifications, streak visibility).
- [x] Data contracts: optional session/usage fields and API spec doc.

**Checkpoint:** Manual QA on all four surfaces; confirm no regressions on auth and sync.

### Phase 2: Metrics and tuning

- Add analytics events and optional backend logging.
- Monitor “time to first action” and goal completion rate.
- Tune copy and default goal/notification values from feedback.

**Checkpoint:** First week of metrics reviewed; adjust defaults if needed.

### Phase 3: Backend adoption (optional)

- Backend supports optional session fields (e.g. `SessionScore`, `DistractionEventCount`).
- Optional daily snapshot and productivity settings endpoints.
- Client uses snapshot/settings when available; falls back to local derivation and Preferences.

**Checkpoint:** Backend contract deployed; client verified against new/old backend.

### Phase 4: Optimization

- Onboarding hints for first-time users (e.g. set goal, start first session).
- Notification calibration based on notification intensity setting.
- A/B or gradual rollout of new Home layout if needed.

---

## Rollback

- Feature toggles are not required for initial release; rollout is client-only.
- If critical issues appear, revert client changes; backend remains backward compatible.
- Preferences keys are additive; clearing them restores defaults without breaking existing data.
