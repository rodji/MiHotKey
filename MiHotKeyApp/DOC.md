# MiHotKey Operational Notes

This document is for user-facing workflows that are not primarily about config schema. For route definitions, target rules, and JSON fields, see `CONFIG.md`.

## Running the App

Typical ways to start MiHotKey:

- `dotnet run --project MiHotKeyApp`
- run the built `MiHotKeyApp.exe`
- run the published `MiHotKeyApp.exe`

When started without arguments, MiHotKey runs as a resident tray application. If another copy is already running from the same base directory, the new copy exits quietly instead of opening a second tray instance.

## Tray Menu

The tray icon provides quick access to the runtime:

- `Reload config`: reloads the current config file without restarting the app
- `Show log`: opens the in-app log window
- `Run`: starts programs listed in the config
- `Run diagnostics`: writes a diagnostics snapshot into the log
- `Foreground tracking`: temporarily enables or disables runtime foreground-history tracking
- `Autostart`: updates the current config and Windows autostart state
- `Exit`: stops the resident instance

## Command-Line Route Invocation

MiHotKey can ask the already-running tray instance to execute a route directly:

```powershell
.\MiHotKeyApp.exe call route notepad
```

This is useful when you want to trigger the same routing logic from scripts, launchers, or manual terminal commands.

Notes:

- The route must already exist in the loaded config.
- The command talks only to the local loopback IPC endpoint for the current user and app directory.
- If the resident tray instance is not running yet, the command tries to start it and waits for IPC readiness before sending the route.

Current behavior when the tray app is not running:

- the command auto-starts a new resident instance
- it waits up to about 10 seconds for IPC readiness
- if bootstrap still fails, it exits with code `10`

## CLI Examples

Basic invocation and exit code capture:

```powershell
$p = Start-Process .\MiHotKeyApp.exe -ArgumentList 'call','route','notepad' -PassThru -Wait
$p.ExitCode
```

Example against an elevated resident app:

```powershell
Get-Process MiHotKeyApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process .\MiHotKeyApp.exe -Verb RunAs
Start-Sleep -Seconds 2
$p = Start-Process .\MiHotKeyApp.exe -ArgumentList 'call','route','notepad' -PassThru -Wait
$p.ExitCode
```

## Exit Codes

- `0`: success
- `2`: invalid arguments
- `10`: resident unavailable after bootstrap attempt
- `11`: IPC unavailable or transport failed
- `12`: internal error
- `20`: route not found
- `21`: route had no matching target
- `22`: route execution failed

## What Diagnostics Include

`Run diagnostics` writes useful runtime state to the log window:

- foreground tracking status and current mode
- current foreground window and previous window
- recent foreground history
- top-level windows snapshot
- audio devices snapshot

Use this when a route is matching the wrong target, foreground history looks suspicious, or audio actions are not hitting the expected device.
