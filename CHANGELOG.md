# Changelog

## 2026-03-04 Logitech lock debounce tuning

### Intention/Task
- Fix missed second trigger on quick consecutive presses of Logitech lock button.
- Keep protection against hold/repeat noise without blocking valid fast re-presses.

### Changed
- Reduced `inputs.logi[*].debounceMs` for `mx.lock` from `800` to `40` in default config.
- Documented the conclusion from runtime logs: `firstDownOnlyUntilUp` already blocks hold-based repeats, while high debounce was suppressing valid rapid second `*.down` events.

## 2026-03-04 Foreground tracking policy and SRP refactor

### Intention/Task
- Make foreground tracking safer when user is inactive (locked/screensaver/idle).
- Keep `AppRuntime` focused by extracting foreground-tracking behavior to a dedicated component.

### Changed
- Added `app.toggleForegroundTracking` policy (`Off`/`Smart`/`AlwaysOn`) and defaulted config to `Smart`.
- Implemented smart suspension conditions for tracking: session lock, running screensaver, user idle timeout.
- Added periodic re-evaluation of tracking state.
- Added Win32 interop for screensaver and idle detection (`SystemParametersInfo`, `GetLastInputInfo`).
- Extracted all foreground tracking logic from `AppRuntime` into `MiHotKeyApp/Targeting/ForegroundTrackingController.cs`.
- Added Codex agent workflow rules and guardrails in `.codex/AGENTS.md`.
- Updated EN/RU config docs for the new policy semantics.

## 2026-02-27 Global-first two-pass routing and depth-based target search

### Intention/Task
- Improve routing determinism for mixed global/non-global shortcuts.
- Replace coarse mode-based target selection with configurable depth by recency.

### Changed
- Replaced `app.targetSelectionMode` with numeric `app.targetSearchDepth`.
- Updated candidate selection to use foreground + history depth.
- Implemented two-pass routing:
  1. global shortcuts first (recent windows + all top-level),
  2. non-global routes second (recent windows only).
- Improved default target rules for Teams/Meet matching.
- Updated EN/RU docs for the new routing and depth model.

## 2026-02-26 Packaging and delivery workflow improvements

### Intention/Task
- Make app delivery and publishing repeatable with minimal manual steps.
- Ensure tray icon is stable in development and published builds.
- Simplify audio config schema usage.

### Changed
- Added VS Code publish task using `FolderProfile`.
- Extended GitHub Actions with publish step and uploaded publish artifact.
- Embedded tray icon resource and switched tray icon loading to app-owned icon.
- Simplified audio device config usage around `deviceIds` and updated related docs/config.
- Added handling for Logitech vendor key release edge case.

## 2026-02-25 Logitech input and audio control expansion

### Intention/Task
- Add robust Logitech trigger support and richer audio mute scenarios.
- Improve operational diagnostics and UI responsiveness.

### Changed
- Added Logitech Raw Input trigger source and test flow.
- Added multi-device audio mute targeting and grouped toggle behavior.
- Made log window output truly bounded to the last 100 visible lines (respecting level filter) and coalesced refresh scheduling to prevent UI slowdown during heavy diagnostics logging.
- Continued stabilization of lock-button behavior and release handling.

## 2026-02-24 Diagnostics and action routing enhancements

### Intention/Task
- Increase observability for window/audio targeting.
- Improve reliability of focus switching and audio diagnostics.
- Expand trigger-to-action mapping capabilities.

### Changed
- Added diagnostics runner from tray with window/audio/foreground-tracker logging.
- Fixed focus activation timing issues and diagnostics logging defaults.
- Added `droplett` binding for mic toggle.
- Added support for launching actions from Logitech input path.

## 2026-02-06 CI and documentation setup

### Intention/Task
- Establish automated validation and bilingual documentation.

### Changed
- Added GitHub Actions workflow for .NET build/test.
- Switched CI runner to Windows.
- Added/updated Russian documentation copies.

## 2026-02-05 Core configuration and routing evolution

### Intention/Task
- Move from prototype routing to production-ready config-driven behavior.
- Improve maintainability and runtime control from tray/config.

### Changed
- Added autostart support and config-driven HKCU Run management.
- Added per-input session policies (locked/remote gating).
- Added unconditional routes (`routesByTrigger` entries without rule).
- Reworked config loading/writing into `ConfigStore`.
- Unified title matching into glob-capable `title` with backward compatibility.
- Added audio action type with Core Audio mute/unmute/toggle support.
- Refactored router and key sender internals for readability and named constants.
- Updated Teams targeting criteria and tray autostart toggle behavior.
- Updated CONFIG docs to track schema and behavior changes.

## 2026-02-04 Initial project bootstrap and baseline features

### Intention/Task
- Create the initial app skeleton with routing, tracking, and tray UX.

### Changed
- Added repository scaffolding (`.gitignore`, `.gitattributes`, README).
- Added solution/project structure and initial runtime.
- Implemented base window targeting, routing, and logging flows.
- Added foreground tracking/history controls and tray integration.
