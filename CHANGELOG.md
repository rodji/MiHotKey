# Changelog

## 2026-03-04 Foreground tracking refactor in AppRuntime

### Intention/Task
- Extract all foreground tracking logic from `AppRuntime` into a dedicated component to keep responsibilities separated.

### Changed
- Added `ForegroundTrackingController` (`MiHotKeyApp/Targeting/ForegroundTrackingController.cs`) to encapsulate:
  - tracking policy evaluation (`Off` / `Smart` / `AlwaysOn`),
  - periodic refresh timer,
  - idle/screensaver/lock checks,
  - foreground tracking status snapshot for diagnostics.
- Simplified `AppRuntime` to delegate foreground tracking behavior to the controller.
- Kept existing runtime behavior and public integration points (tray toggle and diagnostics output).
