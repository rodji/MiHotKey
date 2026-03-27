# Design

## Overview

MiHotKey is a Windows-only resident tray application built on `net8.0-windows` and WinForms. It turns configured triggers into one of three action types:

- send a shortcut
- launch a configured program
- execute an audio-device mute action

The repository keeps documentation split by audience:

- `README.md`: brief entry point
- `CONFIG.md`: config schema and examples
- `MiHotKeyApp/DOC.md`: user workflows and troubleshooting
- `DESIGN.md`: implementation responsibilities, runtime flow, and technical assumptions

## Process Model

`MiHotKeyApp/Program.cs` is the composition root and supports two launch paths:

- no arguments: start the resident tray application
- `call route <name>`: invoke a configured trigger route through the resident instance over local IPC

Invalid command-line arguments exit with a non-zero code and usage text.

Single-instance behavior is enforced by `SingleInstanceLock`. The mutex name, loopback TCP port, and auth token are derived from the app base directory, so separate copies launched from different directories do not share the same resident instance.

If a second no-argument copy is launched from the same base directory while the resident instance is already running, that second process exits successfully without opening another tray icon.

For `call route <name>`:

1. The CLI computes IPC coordinates from the current base directory.
2. It sends a loopback request to an already-running resident instance.
3. If that fails at the transport level, it starts a no-args resident copy of the same executable.
4. Readiness is confirmed by repeated `ping` IPC requests rather than a fixed sleep.
5. The original route request is retried once after readiness succeeds.

## Runtime Composition

`AppRuntime` owns the long-lived runtime services and their lifecycle:

- config loading and validation via `ConfigStore` and `ConfigValidator`
- in-memory log buffering through `RingLogBuffer` and `RingBufferLoggerProvider`
- session-state tracking and autostart registry management
- foreground tracking composition through `ForegroundTracker` and `ForegroundTrackingController`
- route matching and action execution
- input sources and trigger dispatch
- route invocation API for both live triggers and IPC-triggered `call route` requests

`Program.cs` remains intentionally thin around that runtime. It is responsible for:

- process bootstrap and single-instance handling
- loopback IPC server hosting
- tray UI construction and event wiring
- mapping IPC and tray actions back into `AppRuntime`

Startup flow:

1. Load and validate `config.json`, optionally following `app.configPath`. If the initial load fails, startup continues with the in-memory default config and logs the failure.
2. Apply runtime state to logging, autostart, foreground tracking, routing, and trigger sources.
3. Start foreground-tracking policy refresh and input listeners.
4. Return control to `Program.cs`, which then starts the tray UI and local IPC server.

Reload reuses the existing runtime and reapplies configuration in place. If reload fails after startup, the previous working state remains active.

## Configuration and Persistence

The current config model is versioned as `version: 2`.

`ConfigStore` is responsible for reading and writing config files:

- load supports comments and trailing commas
- `ResolveConfigPath` allows `config.json` to redirect to a relative or absolute target file
- autostart edits are written to the resolved config file, not just the bootstrap file

`ConfigValidator` enforces cross-section consistency before a config becomes live. Besides basic shape checks, it verifies things such as:

- supported `version`
- bounds for `app.logBufferSize` and `app.targetSearchDepth`
- unique target-rule IDs and non-empty rule `proc`
- that `routesByTrigger` references known triggers, rules, and action IDs
- Logitech hex-field ranges and per-event policy keys
- audio-device rules such as not combining legacy `deviceId` with `deviceIds`
- shortcut send-mode constraints, including scan-code availability for `Scan` and `Global`

When toggling `app.autostart.enabled` from the tray, `ConfigStore.TrySetAutostartEnabled` first tries an in-place text patch to preserve comments, ordering, and spacing. If that fails, it falls back to parse-and-write with indented JSON.

`AutostartManager` applies the effective autostart state to the current-user HKCU Run key.

## Input Pipeline

All inputs are normalized into logical trigger IDs before routing.

Global hotkeys already name logical trigger IDs directly. WMI and Logitech sources instead produce mapped low-level events which are then translated through `bindings`.

`TriggerDispatcher` coordinates:

- `GlobalHotkeySource` for Windows-registered global shortcuts
- `WmiTriggerSource` for WMI-driven hardware/system events
- `LogiTriggerSource` for Logitech Raw Input events

Bindings map low-level source events such as `triangle.down` or Logitech event IDs into logical trigger IDs such as `mute`. Trigger delivery is posted onto the UI synchronization context so routing and tray-adjacent operations run on a consistent thread.

Input sources can optionally apply session-aware gating. WMI and Logitech inputs use `SessionState` to suppress events when config requires an unlocked or local session.

For WMI and Logitech subscriptions, repeat suppression is also part of the input layer. The current built-in behavior is `repeatHandling: "firstDownOnlyUntilUp"` backed by `RepeatGate`, with `debounceMs` used to absorb noisy replays without changing the higher-level trigger model.

`LogiTriggerSource` has two mapping layers:

- direct raw report matching such as `hid:hex:...`
- synthesized symbolic candidates for Logitech vendor key packets, for example `logi:vendor-key:0x6F.down`

For the observed MX Keys Bluetooth vendor packet shape (`11 FF 08 00 00 <code> ...`), the source tracks the last non-zero key code per Raw Input device. When the generic zero packet arrives, it can emit the corresponding key-specific `.up` candidate instead of treating that packet as a shared "any key released" event.

## Targeting and Foreground Tracking

Window targeting is history-first.

`ForegroundTracker` records recent foreground windows through `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. It intentionally filters system surfaces such as the taskbar, task-switcher UI, and desktop shell windows so they do not pollute recent-window history.

`ForegroundTrackingController` manages whether the hook should stay active:

- `Off`: disabled
- `AlwaysOn`: enabled whenever target search depth requires history
- `Smart`: automatically suspends tracking while the session is locked, the screen saver is running, or the user has been idle for about one minute

`app.targetSearchDepth` is the main dial for recent-window targeting:

- `1`: use only the current foreground window and keep tracking disabled
- `2+`: use foreground plus recent history, with tracker capacity derived from depth

That means the foreground-tracking policy only matters when depth is greater than `1`.

The tray menu can temporarily override the configured policy without editing the config file.

`TargetSelector` returns recent-window candidates up to `app.targetSearchDepth`. `WindowInfoProvider` resolves metadata such as process name, class name, title, and top-level window enumeration. `WindowRuleMatcher` evaluates target rules by descending priority using:

- process name
- class name
- title patterns and backward-compatible title fields

## Routing Model

Routing is driven by `routesByTrigger`.

Each trigger ID maps to one or more routes. A route references:

- a target rule ID, or an empty rule for unconditional execution
- an `actionType` (`Shortcut`, `Program`, `Audio`)
- an `actionId` inside the corresponding config section

`Router` exposes both:

- `HandleTrigger(...)` for fire-and-forget trigger delivery from live inputs
- `InvokeTrigger(...)` for result-bearing execution used by IPC and command-line route calls

It returns structured `RouteInvocationResult` values:

- `Success`
- `MissingTrigger`
- `NoMatch`
- `ExecutionFailed`

Routing order is:

1. Check whether the trigger has an unconditional route (`rule` empty).
2. Search recent foreground-history candidates up to `app.targetSearchDepth`.
3. For each candidate window, evaluate matching rules in descending priority order.
4. If nothing matches, run a global fallback pass across top-level windows, but only for shortcut routes whose send mode is `Global`.

The two routed passes use different precedence:

- history pass: newer windows win first; within one window, higher-priority matching rules win first
- global fallback: higher-priority global-capable rules win first; each such rule then scans top-level windows until it finds a match

Important routing semantics:

- a matching target rule without a route for the current trigger is skipped, not treated as terminal
- unconditional routes can execute shortcuts, programs, or audio actions without window matching
- global fallback is intentionally narrow and does not apply to program or audio routes
- recent-history matching is attempted before the global fallback pass, so a recent non-global target can win over an older global-only candidate
- `HandleTrigger(...)` and `InvokeTrigger(...)` share the same routing logic; the latter just preserves a structured result for IPC and CLI callers

## Action Execution

### Shortcuts

Shortcut execution is handled by `KeySender`.

Supported send modes:

- `Scan`: layout-independent `SendInput` using scan codes
- `Vk`: layout-dependent `SendInput` using virtual keys
- `Messages`: targeted message-based send to a specific window
- `Global`: broadcast-style input path that does not depend on a matched target window

`FocusController` temporarily activates the matched window when the configured focus policy requires it. `NoFocusChange` mode refuses focused-window sends when the matched window is not currently foreground.

### Programs

`ProgramRunner` starts configured programs from the `programs` section. Program definitions can specify working directory, shell execute behavior, hidden startup, output capture, and environment variables. Tray-launched programs and route-launched programs share the same execution path.

### Audio

`AudioDeviceManager` resolves audio device targets from the `audioDevices` section and applies mute-related actions:

- `ToggleMute`
- `Mute`
- `Unmute`

Config can target capture or render flow, choose a role, and resolve targets in one of three ways:

- `scope: Single` with empty `deviceIds`: use the default device for the configured role
- `scope: Single` with `deviceIds`: operate on the explicitly listed devices
- `scope: AllActiveInFlow`: operate on every active endpoint in the selected flow

For batch operations, toggle semantics are group-aware: if any targeted device is currently unmuted, `ToggleMute` mutes the whole batch; otherwise it unmutes the whole batch.

## UI and Diagnostics

The resident UX is a notify-icon tray app implemented by `TrayAppContext`.

Current tray responsibilities:

- reload config
- show the in-process log window
- run configured programs
- run diagnostics
- toggle foreground tracking
- toggle autostart
- exit the app

The `Foreground tracking` tray checkbox is a temporary runtime override layered on top of `app.toggleForegroundTracking`. It does not rewrite config.

`LogWindowPresenter` and `RingLogBuffer` provide a lightweight live log viewer, but they do not mirror the full in-memory ring buffer into the textbox. The UI shows only the last 100 entries that pass the current level filter, and refresh scheduling is coalesced so heavy diagnostics logging does not enqueue a repaint for every appended line.

`AppRuntime.RunDiagnostics()` logs:

- current foreground-tracking status and policy inputs
- current foreground and previous windows
- tracked foreground history snapshot
- top-level windows
- audio device snapshot

For each enumerated audio endpoint, diagnostics also emit a copy-friendly `audio config ...` helper line that can be used as a starting point when building `audioDevices` entries.

The top-level window listing can preserve tab-order-like enumeration or be sorted, depending on `app.diagnostics.sortByTabOrder`.

## IPC Design

The resident instance hosts a loopback TCP server in `AppCommandPipeServer`. The name is historical; transport is TCP over loopback, not a named pipe.

Protocol characteristics:

- one JSON request per line
- one JSON response per line
- loopback-only transport
- per-base-directory, per-user auth token

Current IPC commands:

- `call-route`
- `ping`

The IPC server posts command handling back onto the UI synchronization context before touching runtime services. This keeps runtime operations on the same thread model used by the tray app and Win32-interacting components.

## Packaging and Runtime Assumptions

- target framework: `net8.0-windows`
- UI stack: WinForms
- tray icon is embedded as an assembly resource and also used as the application icon
- `MiHotKeyApp/config.json` is copied to build and publish output
- the checked-in `FolderProfile` publish profile targets framework-dependent single-file `win-x86` output with ReadyToRun enabled
- the default publish output path is `MiHotKeyApp/bin/Release/net8.0-windows/publish/win-x86/`
- the resident app and CLI bootstrap path rely on loopback TCP being available on the local machine
- expected verification commands are `dotnet build` and `dotnet publish -c Release`

The app assumes a Windows desktop session with access to Win32 APIs, WMI, Raw Input, Core Audio APIs, the user HKCU registry, and a functioning local loopback interface. It is not designed for cross-platform execution.
