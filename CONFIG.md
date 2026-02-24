# `config.json` Configuration (v2)

The app reads `config.json` from the startup folder (this is `AppContext.BaseDirectory`). JSON is parsed **case-insensitively** for keys, with support for `//`/`/* */` comments and trailing commas.

## What's new in v2

- `version: 2` (v1 is no longer supported).
- `routesByTrigger[*]` now uses `actionType`/`actionId` instead of `shortcut`.
- Added `programs` and program launch from the UI/routing.
- Added `shortcuts[*].send = "messages"` (via `PostMessage`).
- Added `app.autostart.enabled` (start on user login via HKCU Run).
- Added matching options in `targets.rules` by window class (`classIs`) and window title (`title`, glob patterns).
- Added policies in `inputs.wmi` for "allow triggers while locked/remote" (`sessionPolicy`, `sessionPolicyByEvent`).
- In `routesByTrigger`, support for "no rule" (empty/missing `rule`) - unconditional action.
- Added `audioDevices` and `routesByTrigger.actionType = "Audio"` to control mic/speaker mute.
- Added diagnostics and the tray menu item **Run diagnostics** (logs windows/audio/foreground-tracker).

## How the config file is chosen

Loading algorithm:

1. Always read `config.json` next to the `.exe` first (to get `app.configPath`).
2. If `app.configPath` points to another path, that file is loaded and used as the final config.

This lets you keep the "main" config, for example, in `%AppData%`, leaving a small bootstrap config next to the `.exe`.

## Top-level structure

- `version` - must be `2`.
- `app` - app behavior (focus, window selection, send timings).
- `tray` - visibility of tray menu items.
- `logging` - logging levels.
- `inputs` - event sources (hotkeys, WMI, Logitech Raw Input/HID).
- `bindings` - mapping `triggerId -> [events...]` for WMI events.
- `targets` - rules for finding the target window.
- `shortcuts` - dictionary of key combinations to send.
- `programs` - dictionary of programs that can be launched.
- `audioDevices` - actions on audio devices (mute/unmute/toggle).
- `routesByTrigger` - what to do for each (trigger, rule) pair.

## `app`

```jsonc
{
  "app": {
    "configPath": ".\\config.json",
    "altConfigPathHint": "%AppData%\\MiHotKey\\config.json",
    "logBufferSize": 100,
    "foregroundTrackingEnabled": true,
    "foregroundHistorySize": 10,
    "targetSelectionMode": "ForegroundThenPrevious",
    "focusPolicy": "ActivateTargetTemporarily",
    "sendTimingMs": { "modDownToKeyDown": 5, "keyDownToKeyUp": 2, "keyUpToModUp": 2 },
    "autostart": { "enabled": false },
    "diagnostics": { "sortByTabOrder": false }
  }
}
```

- `configPath` - path to the "main" config (absolute or relative to the startup folder).
- `altConfigPathHint` - hint in logs (does not affect behavior).
- `logBufferSize` - size of the log ring buffer (10..10000).
- `foregroundTrackingEnabled` - enable tracking of the current/previous window.
- `foregroundHistorySize` - how many windows to keep in history (0..1000).
- `targetSelectionMode` - target selection strategy. Values: `ForegroundThenPrevious` - first the current foreground window, then the previous one; `ForegroundOnly` - only the foreground; `AlwaysPrevious` - only the previous.
- `focusPolicy` - focus behavior when sending. Values: `ActivateTargetTemporarily` - temporarily activate the target window to send (then restore focus); `NoFocusChange` - do not change focus (only if the target window is already foreground).
- `sendTimingMs` - delays (ms) between modifier down/up and the main key.
- `autostart.enabled` - enable autostart on Windows user login. Implementation: write `MiHotKey` to `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` with the current `.exe` command line. Can be toggled from the tray menu **Autostart**; the app updates `config.json` and reloads the config.
- `diagnostics.sortByTabOrder` - ordering of top-level windows in diagnostics. Values: `true` - Z order (Alt-Tab order); `false` - sort by `proc`/`title`.

## `tray`

Controls visibility of tray menu items.

```jsonc
{ "tray": { "reloadConfig": true, "showLog": true, "toggleForegroundTracking": true, "runDiagnostics": true, "exit": true } }
```

- `runDiagnostics` - show the **Run diagnostics** item (logs top-level windows, audio devices, and foreground-tracker state).

## `logging`

```jsonc
{
  "logging": {
    "level": "Information",
    "overrides": { "Target": "Debug", "Match": "Debug", "Send": "Debug", "Exec": "Information" },
    "includeScopes": false,
    "maxMessageLength": 300,
    "showConfigPathsInLog": true
  }
}
```

- `level` - base level.
- `overrides` - per-category overrides.
- `maxMessageLength` - truncate messages in the UI log.
- `showConfigPathsInLog` - log config paths on reload.

## `inputs.hotkeys`

Global hotkeys (via `RegisterHotKey`). Registered with `MOD_NOREPEAT` (long press does not spam repeats).

```jsonc
{ "inputs": { "hotkeys": [ { "id": "mute", "keys": "Ctrl+Alt+M" } ] } }
```

`keys` format: modifiers `Ctrl`, `Alt`, `Shift`, `Win` (any order). Main key: a letter/digit (`M`, `1`) or a name from `System.Windows.Forms.Keys` (for example, `F12`, `Escape`).

## `inputs.wmi` / `inputs.logi` + `bindings`

The WMI and Logitech sources generate "events" (strings), which are then mapped to `triggerId` via `bindings`.

Example:

```jsonc
{
  "inputs": {
    "wmi": [
      {
        "id": "tma",
        "namespace": "root\\wmi",
        "query": "SELECT * FROM TMA_WMIEvent",
        "where": { "InstanceName": "ACPI\\PNP0C14\\0x2_0" },
        "extract": { "prop": "EventDetail", "index": 2 },
        "map": { "1": "triangle.down", "6": "triangle.up" },
        "sessionPolicy": "Any",
        "sessionPolicyByEvent": {
          "triangle.down": "RequireUnlockedLocalSession"
        },
        "repeatHandling": "firstDownOnlyUntilUp",
        "debounceMs": 40
      }
    ]
  },
  "bindings": {
    "mute": [ "triangle.down" ]
  }
}
```

- `map` - string code to event name.
- `bindings` - `triggerId` to list of events that should trigger that `triggerId`.
- `sessionPolicy` - policy for "when it is allowed to accept events" for this source.
- `sessionPolicyByEvent` - policy override for a specific `mappedEvent` (keys are **values** from `map`, for example `triangle.down`).

`sessionPolicy` / `sessionPolicyByEvent` values: `Any` - always accept; `RequireUnlocked` - do not accept while the workstation is locked; `RequireLocalSession` - do not accept if the app is running in a Remote Desktop (RDP) session; `RequireUnlockedLocalSession` - combination of both conditions.

Notes:

- "Locked" is determined by `SessionLock`/`SessionUnlock` events of the current user session.
- "Remote" is determined by `SystemInformation.TerminalServerSession` (i.e., RDP session).

## `inputs.logi` + `bindings` (Logitech keyboards / HID)

This source listens to Windows Raw Input and can map either keyboard scan/vk events or raw HID report bytes to events.

It is intended for Logitech devices (`VID_046D`) such as MX Keys. For programmable keys that require HID++ diversion, use Logitech Options+/Solaar to divert the key first; MiHotKey currently listens for the resulting input reports and maps them.

Example (diagnostic-first):

```jsonc
{
  "inputs": {
    "logi": [
      {
        "id": "mxkeys",
        "kind": "Hid",
        "vendorId": "046D",
        "productId": "B35B",
        "usagePage": 65280,
        "usage": 1,
        "devicePathContains": [ "VID_046D&PID_B35B" ],
        "map": {
          "hid:hex:11FF0101*": "mx.smartactions1.down",
          "hid:hex:11FF0100*": "mx.smartactions1.up"
        },
        "logRaw": true,
        "repeatHandling": "firstDownOnlyUntilUp",
        "debounceMs": 40
      }
    ]
  },
  "bindings": {
    "mute": [ "mx.smartactions1.down" ]
  }
}
```

Mapping keys in `inputs.logi[].map`:

- Keyboard messages: `kbd:vk:0xAF.down`, `kbd:vk:175.down`, `kbd:scan:0x6A.down`, `kbd:scan:0x6A.e0.up`
- Raw HID reports: `hid:hex:11FF010100000000`
- Prefix match is supported with a trailing `*` (for example `hid:hex:11FF01*`)

Fields:

- `id` - source id (for logs only; routing still goes via `bindings`)
- `kind` - `Any`, `Keyboard`, or `Hid`
- `vendorId` / `productId` - hex USB IDs (default vendor is Logitech `046D`)
- `devicePathContains` - optional substrings to narrow to one interface (OR match)
- `usagePage` / `usage` - optional Raw Input HID top-level collection filter (integers, often useful for vendor-defined HID++ channels)
- `map` - raw pattern -> event
- `logRaw` - log every matching/raw report for discovery
- `sessionPolicy`, `sessionPolicyByEvent`, `repeatHandling`, `debounceMs` - same semantics as `inputs.wmi`

## `targets.rules`

Rules for finding the target window. When a trigger fires, the app iterates candidates (see `targetSelectionMode`) and picks the first window that matches the rule.

```jsonc
{
  "targets": {
    "rules": [
      {
        "id": "teams",
        "prio": 95,
        "proc": [ "msedgewebview2", "ms-teams" ],
        "classIs": [ "TeamsWebView" ],
        "title": [ "* | Microsoft Teams" ]
      }
    ]
  }
}
```

- `id` - rule identifier (used in `routesByTrigger`).
- `prio` - priority (higher = more important).
- `proc` - process names without `.exe` (for example `chrome`).
- `classIs` - list of allowed window class names (from `GetClassNameW`).
- `title` - list of patterns for the window title (OR: one match is enough). Glob support: `*` = any chars, `?` = one char, `\\*`/`\\?` = literal. If a pattern contains no `*`/`?`, it is treated as a substring (equivalent to `*text*`). For an exact match, use the `=` prefix, for example `=Meeting`.

Matching:

- first `proc` (required),
- then (if set) `classIs`,
- then (if set) `title`.

## `shortcuts`

Dictionary of "key combination" to how to send.

```jsonc
{
  "shortcuts": {
    "teams.mute": { "keys": "Win+Alt+K", "send": "global" }
  }
}
```

`send` values: `scan` - `SendInput` by scan-code (usually **layout-independent**; requires the key to be in the scan-code mapping; currently mapping exists for A..Z). `vk` - `SendInput` by virtual key (**layout/language-dependent** for the active input). `messages` - send `WM_KEYDOWN/WM_KEYUP` via `PostMessage` to the target window **without changing focus** (does not work in every app, but often helps when modifier conflicts with a global hotkey). `global` - `SendInput` without focusing the window (like `scan`), but only if the target window exists among top-level windows (useful for apps that register global hotkeys themselves).

The `keys` format is the same as in `inputs.hotkeys` (modifiers: `Ctrl`, `Alt`, `Shift`, `Win`).

## `programs`

Dictionary of programs that can be launched (from the tray menu `Run` or via routing).

```jsonc
{
  "programs": {
    "notepad": {
      "title": "Notepad",
      "file": "notepad.exe",
      "args": "",
      "workdir": "",
      "useShellExecute": true,
      "hidden": false,
      "captureOutput": false,
      "env": {}
    }
  }
}
```

Fields:

- `title` - name in UI (if empty, `id` is shown).
- `file` - path/command. Supports environment variables (`%ComSpec%`, `%AppData%`). If the path is relative (contains `.` or `\\`/`/`), it is resolved relative to the startup folder; otherwise it is passed as-is (for example `notepad.exe` is found via PATH).
- `args` - command-line arguments (with `%VAR%`).
- `workdir` - working folder (same resolution rules as `file`).
- `useShellExecute` - launch via shell (like in Explorer) if `true`; direct process start (with `env` and stdout/stderr capture) if `false`.
- `hidden` - attempt to launch hidden (relevant for console apps when `useShellExecute=false`).
- `captureOutput` - log stdout/stderr after completion (only when `useShellExecute=false`).
- `env` - environment variables (only when `useShellExecute=false`).

The log includes: start, completion, `exit code`, stdout/stderr (trimmed to a reasonable size).

## `audioDevices`

Actions on audio devices via Core Audio (Windows).

```jsonc
{
  "audioDevices": {
    "mic.toggle": {
      "flow": "Capture",
      "role": "Communications",
      "deviceId": "",
      "action": "ToggleMute"
    }
  }
}
```

Fields:

- `flow` - stream type. Values: `Capture` - microphones (input); `Render` - speakers/headphones (output).
- `role` - device role. Values: `Console` - default "general" device; `Multimedia` - multimedia; `Communications` - communications (often used by Teams/Zoom).
- `deviceId` - specific device ID (Windows string). If empty, the default for `flow`+`role` is used.
- `action` - action to perform. Values: `ToggleMute` - toggle mute; `Mute` - mute; `Unmute` - unmute.

Notes:

- If `deviceId` is set, `role` is ignored.
- Mute is applied at the device level and affects all apps using that device.

## `routesByTrigger`

Main routing table: for each `triggerId`, a list of rules is defined, and for each rule an action is configured.

```jsonc
{
  "routesByTrigger": {
    "mute": [
      { "rule": "teams", "actionType": "Shortcut", "actionId": "teams.mute" },
      { "rule": "meet",  "actionType": "Shortcut", "actionId": "meet.mute" }
    ],
    "openNotepad": [
      { "actionType": "Program", "actionId": "notepad" }
    ]
  }
}
```

- `rule` - `targets.rules[].id`. If the field is missing or empty (`""`), the action is executed **unconditionally**, without window matching.
- `actionType` - action type. Values: `Shortcut` - send `shortcuts[actionId]`; `Program` - launch `programs[actionId]`; `Audio` - perform action from `audioDevices[actionId]`.
- `actionId` - action id (key in `shortcuts`, `programs`, or `audioDevices`).

Important:

- `triggerId` must exist in `inputs.hotkeys[].id` **or** in `bindings` (as a key). For example, for `openNotepad` above you need to add `inputs.hotkeys` with `id: "openNotepad"` or add a binding via `bindings`.
- The first matching window executes the action and handling stops.
