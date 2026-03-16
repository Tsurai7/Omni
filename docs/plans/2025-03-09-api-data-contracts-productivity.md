# API and Data Contract Extensions for Productivity Features

**Date:** 2025-03-09  
**Status:** Spec for backend adoption; client models already extended with optional fields.

## Overview

Optional fields and future endpoints to support Daily Pulse, streaks, session scoring, and coaching insights. All new fields are optional for backward compatibility.

---

## Session API

### SessionSyncEntry (client → backend)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| GoalId | string | No | Links session to a daily goal for analytics. |
| GoalTargetMinutes | int? | No | Target duration in minutes (e.g. 25, 60). |
| SessionScore | int? | No | Concentration score 0–100 from distraction tracking. |
| DistractionEventCount | int? | No | Number of distraction events during the session. |

Existing: `Name`, `ActivityType`, `StartedAt`, `DurationSeconds`.

### SessionListEntry (backend → client)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| SessionScore | int? | No | Concentration score when stored by backend. |
| DistractionEventCount | int? | No | Distraction event count when stored. |

---

## Usage API

No changes to `UsageListEntry` / `UsageListResponse` required for current insights. Client derives “focus minutes today”, “biggest distraction”, and “trend vs last week” from existing usage data.

### Future: Daily productivity snapshot (for Home Pulse)

Optional endpoint for a single-call Home payload:

- **GET** `/api/productivity/daily-snapshot?date=yyyy-MM-dd`
- **Response (proposed):**
  - `FocusMinutesToday`: int
  - `GoalMinutes`: int
  - `GoalCompleted`: bool
  - `StreakDays`: int
  - `DistractionMinutesToday`: int (optional)

If not implemented, client continues to derive focus minutes from existing `GetUsageAsync(from, to, "day", ...)` and uses local preferences for goal minutes and placeholder streak.

---

## User productivity settings

Optional endpoint for syncing preferences across devices:

- **GET** `/api/productivity/settings`
- **PUT** `/api/productivity/settings`
- **Body (proposed):** `DailyGoalMinutes`, `NotificationIntensity`, `StreakVisible`, `FocusHoursStart`, `FocusHoursEnd`

If not implemented, client keeps using local `ProductivityPreferences` (Preferences API).

---

## Coaching insights (optional)

Optional endpoint for server-generated recommendations:

- **GET** `/api/productivity/insights?from=yyyy-MM-dd&to=yyyy-MM-dd`
- **Response (proposed):** list of insight objects, e.g. `BiggestDistractionCategory`, `BestFocusDayOfWeek`, `TrendVsLastWeek`, `RecommendationChipText`, `RecommendationAction`.

If not implemented, client continues to compute insights from usage data as in `UsageStatsPage.UpdateInsightsAndRecommendation`.

---

## Backward compatibility

- Clients must treat all new fields as optional and omit them when not set.
- Backends should ignore unknown fields and not require new fields for existing endpoints.
- Once backend supports a field (e.g. `SessionScore` on list), client can display it without contract change.
