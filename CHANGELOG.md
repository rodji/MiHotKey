# Changelog

## 2026-04-20 Window-title timeout hardening for tray responsiveness

### Intention/Task
- Prevent a hung or slow external window from freezing the resident tray app during routing.
- Make post-resume / post-hibernate window inspection safer without needing to identify a specific offending application.

### Changed
- Replaced unbounded window-title reads with a timeout-based title query using `SendMessageTimeoutW(..., SMTO_ABORTIFHUNG, ...)`.
- Added an early hung-window check via `IsHungAppWindow` before attempting title retrieval.
- Updated `WindowInfoProvider` so callers can request only the metadata they actually need instead of always fetching process, class, and title together.
- Reworked routed matching to load process/class data first and only fetch window titles when a candidate rule actually requires title matching.
- Reworked global fallback to scan windows lazily instead of materializing full `WindowInfo` for every top-level window up front.
- Added warning logs when window-title retrieval times out so future investigations have a visible breadcrumb without sacrificing responsiveness.

### Why
- The tray UI, hotkey window, and routing work all share the WinForms message loop, so one blocking Win32 window-inspection call could make the whole app appear hung.
- Timeout-based title reads and staged metadata loading reduce the chance that a broken or recovering window can stall the app after sleep, hibernate, or other shell instability.

## 2026-03-27 Documentation maintenance, design doc, and operational notes

### Task
- Merge stronger Codex workflow rules into `.codex/AGENTS.md`.
- Restore missing technical design documentation and improve user-facing non-config docs.
- Re-sync `DESIGN.md` with the current `version: 2` implementation after routing, diagnostics, Logitech, audio, and IPC evolved further.
- Tighten `DESIGN.md` again where runtime responsibilities and routing precedence drifted from the actual code.

### Changed
- Expanded `.codex/AGENTS.md` with required updates for `CHANGELOG.md`, `DESIGN.md`, and `MiHotKeyApp/DOC.md`.
- Refined `.codex/AGENTS.md` so `dotnet build` and `dotnet publish -c Release` are required only for code/project changes, not for documentation-only edits.
- Added a new `DESIGN.md` that documents the current runtime architecture, routing flow, single-instance behavior, IPC path, tray responsibilities, and packaging assumptions.
- Reworked `MiHotKeyApp/DOC.md` into user-facing operational notes for tray usage, CLI route calls, diagnostics, and exit codes.
- Updated `DESIGN.md` again to match the current code: `routesByTrigger`, unconditional routes, Logitech input, foreground-tracking policy controller, audio/program action execution, and loopback-TCP IPC details.
- Clarified in `DESIGN.md` that `Program.cs` owns process bootstrap / IPC / tray wiring, `app.targetSearchDepth=1` disables foreground history tracking, and routing remains history-first with a narrow global-shortcut fallback.
- Completed the design notes for Logitech vendor-key release decoding, symbolic `logi:vendor-key:*` mappings, audio batch targeting semantics, and loopback bootstrap assumptions.
- Tightened `DESIGN.md` around input repeat/debounce handling, the real log-window behavior (last 100 filtered lines with coalesced refresh), and the checked-in `FolderProfile` publish assumptions (`win-x86`, single-file, ReadyToRun).
- Completed `DESIGN.md` with the remaining runtime details that were still easy to infer only from code: default-config startup fallback, validator responsibilities, how hotkeys differ from bound source events, and the helper audio-config lines emitted by diagnostics.

### Why
- Keep repository instructions aligned with the way the project is now maintained.
- Separate config schema, user workflows, and technical design so each doc stays focused and easier to keep current.
- Prevent the design document from drifting behind the actual runtime behavior and config model.
- Make the design doc dependable during future refactors by documenting the real routing precedence instead of older experiments.

## 2026-03-23 History-first routing with global fallback

### Intention/Task
- Make recent window history the primary routing signal for mixed Meet/Teams-style scenarios.
- Keep `global` shortcuts as a narrow fallback instead of letting any matching top-level app preempt history.

### Changed
- Reworked routing order to scan recent windows first and evaluate matching target rules by descending `prio` within each window.
- Stopped treating a matched target rule without a route for the current trigger as terminal; routing now skips it and continues searching.
- Added a dedicated top-level fallback pass that only considers routes whose shortcut uses `send: "global"`.
- Extended `WindowRuleMatcher` with ordered-match helpers used by the new routing flow.
- Updated EN/RU config docs to describe the history-first plus global-fallback behavior.

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
