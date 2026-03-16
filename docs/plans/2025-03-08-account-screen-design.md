# Account screen design

**Date:** 2025-03-08

## Summary

Add an Account flyout screen to the Omni client showing current user (email from `GET /api/auth/me`), Sign out, and placeholders for "Change password" and "Delete account" (Coming soon). Light optimizations: session cache for `/me`, transient Account page.

## Decisions

- **Placement:** New flyout item "Account" (Approach 1).
- **Content:** Email, Sign out button, two placeholder rows (Change password, Delete account) with "Soon".
- **Auth:** `IAuthService.GetCurrentUserAsync()` calling `GET /api/auth/me`; in-memory cache per session (cleared on Logout).
- **Optimization:** Account page transient; `/me` called once per session and cached.

## Implementation

- `IAuthService` / `AuthService`: add `GetCurrentUserAsync()`, call backend `/api/auth/me`, cache `UserResponse` in `_cachedUser`, clear on `Logout()`.
- `AccountPage.xaml` / `AccountPage.xaml.cs`: dark theme, email label, loading indicator, Sign out (stops sync, logout, navigate to Login), placeholders.
- Shell: new FlyoutItem "Account", route `AccountPage`; register in `AppShell.xaml.cs`.
- DI: `AddTransient<AccountPage>()`.
- AccountPage uses parameterless ctor and resolves `IAuthService` (and optional `IUsageService`) from `MauiProgram.AppServices` to match LoginPage and Shell DataTemplate creation.
