# MiHotKey

A small Windows tray utility that binds triggers (global hotkeys, WMI events) to actions on a target window: sending key combinations or launching a program. It also supports custom keyboard keys on the <b>Xiaomi Mi Gaming Laptop</b>.

## Quick start

- Build: `dotnet build MiHotKey.sln -c Release`
- Run from source: `dotnet run --project MiHotKeyApp`
- Config: `MiHotKeyApp/config.json` is copied to the output folder and read by the app on startup.

## Configuration

Schema and examples are in `CONFIG.md`.

Operational notes are in `MiHotKeyApp/DOC.md`.

Technical design notes are in `DESIGN.md`.
