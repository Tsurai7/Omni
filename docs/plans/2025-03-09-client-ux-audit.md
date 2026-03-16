# Client UX Audit: Motivation Loop Gaps

**Date:** 2025-03-09  
**Scope:** Home, Session, Usage Stats, Account screens in Omni.Client (MAUI).

## Summary

The app currently provides passive activity tracking and session history but lacks goal framing, immediate feedback loops, streak/continuity signals, and actionable recommendations. The distraction scoring pipeline exists (`ISessionDistractionService` / `SessionScoreResult`) but is not surfaced in the UI.

---

## Home (MainPage)

| Gap | Current state | Impact |
|-----|---------------|--------|
| No daily goal | Only "Total tracked time" and "Current category"; no target or progress | User cannot answer "Am I on track today?" in under 5 seconds |
| No streak | No consecutive-days or habit continuity signal | Weak motivation to return daily |
| No single CTA | No "Start Focus" or "Recover Focus" primary action | Friction to start a session from Home |
| Passive "Now" | Grouped app list by category; no clear "current activity" state (Focus / Neutral / Distraction) | No at-a-glance behavioral nudge |
| No next best action | No recommendation row (e.g., "Block Social for 25 min") | Missed Rize-style coaching moment |

**Files:** `MainPage.xaml`, `MainPage.xaml.cs` — bindings: `TotalTrackedTime`, `CurrentCategory`, `GroupedApps`; no goal, streak, or CTA bindings.

---

## Session (SessionPage)

| Gap | Current state | Impact |
|-----|---------------|--------|
| No pre-session intent | Duration, name, type only; no goal link or distraction policy | Session feels untethered from daily progress |
| Score not shown | `SessionScoreResult` (Score, Summary, DistractionEventCount) produced by `ISessionDistractionService` but never subscribed in SessionPage | User misses post-session reinforcement |
| No post-session reflection | After stop: list refreshes only; no sheet/summary or reflection prompt | Lost "close the loop" moment |
| No milestones | No first-session-of-day, goal-reached, or streak-saved micro-celebrations | Fewer dopamine hooks |

**Files:** `SessionPage.xaml`, `SessionPage.xaml.cs` — no subscription to `SessionEndedWithScore`; no score/reflection UI.

---

## Usage Stats (UsageStatsPage)

| Gap | Current state | Impact |
|-----|---------------|--------|
| No insight cards | Pie/bar charts + grouped list only; no "Biggest distraction", "Best focus window", "Trend vs last week" | Analytics without coaching |
| No recommendation chips | No actionable suggestions (e.g., block category for 25 min) | Passive data only |
| Dense by default | View/filter pickers and charts visible at once | Could use progressive disclosure for calmer default |

**Files:** `UsageStatsPage.xaml`, `UsageStatsPage.xaml.cs` — charts and filters present; no insight or recommendation models/UI.

---

## Account (AccountPage)

| Gap | Current state | Impact |
|-----|---------------|--------|
| No productivity preferences | "Coming soon" placeholders; no daily goal, notification intensity, focus hours, or streak visibility | User cannot tune motivation/feedback loop |

**Files:** `AccountPage.xaml`, `AccountPage.xaml.cs` — email + sign out only; placeholders for Change password / Delete account.

---

## Data / Backend

| Gap | Current state | Impact |
|-----|---------------|--------|
| No goal or streak in models | `SessionSyncEntry`, `SessionListEntry`, `UsageListEntry` carry only raw duration/category/app | Cannot drive Daily Pulse, streak, or insights without new fields or aggregates |
| No daily snapshot API | No endpoint for "today's focus minutes, goal progress, streak" for Home | Daily Pulse would need to be derived client-side only (e.g., from sessions + usage) or backend support added |
| No coaching insights API | No endpoint for "biggest distraction", "best focus window", "trend vs last week" | Insight cards would need to be computed client-side from existing usage/session data or new API |

**Files:** `SessionModels.cs`, `UsageModels.cs` — no `GoalId`, `GoalTargetMinutes`, `SessionScore`, `StreakDays`, `FocusMinutesToday`, etc.

---

## Nudges and Distraction

| Gap | Current state | Impact |
|-----|---------------|--------|
| Nudge config not in UI | `DistractionConfig` and notification behavior not user-tunable from Account or Session | Users cannot adjust sensitivity or quiet hours |
| No "why I was nudged" | Notifications fire from `SessionDistractionService` but no in-app explanation or history | Transparency and trust gap |

**Files:** `DistractionConfig.cs`, `SessionDistractionService.cs`, notification services — no settings surface in Account/Session.

---

## Next Steps (aligned with plan)

1. **Design system** — Shared tokens and card components for consistent, Apple-inspired visuals.
2. **Home** — Daily Pulse (today focus, goal ring, streak, one CTA), "Now" state, next best action.
3. **Session** — Pre-session intent, post-session score sheet + reflection, milestones.
4. **Usage Stats** — Insight cards, recommendation chips, progressive disclosure.
5. **Account** — Productivity preferences (goal, notifications, focus hours, streak visibility).
6. **Data contracts** — Optional fields and APIs for goals, streaks, daily snapshot, insights.
7. **Metrics & rollout** — Instrumentation and phased rollout checkpoints.
