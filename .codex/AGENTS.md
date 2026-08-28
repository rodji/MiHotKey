# Agent Instructions

## Coding Principles

- Follow **Single Responsibility Principle**.
- Do not put unrelated functionality into one file/class/method.
- Prefer small focused abstractions over large "god" files.

## Documentation Maintenance

When you change code, behavior, runtime, packaging, validation, UI, workflows, or repository tooling, update the docs in the same task.

### Always Update `CHANGELOG.md`

Update `CHANGELOG.md` (ENG) whenever there is any meaningful project change, including:

- code behavior changes
- UI changes
- validation/scanner changes
- runtime or packaging changes
- tooling and repository file changes
- documentation structure changes

How to update it:

- prefer updating the newest entry for the current date if it already exists
- otherwise add a new entry at the top with the current date
- keep it short and practical
- use these sections when they help:
  - `Added`
  - `Changed`
  - `Task`
  - `Why`
- describe task, outcome and intent, not a full file-by-file dump

### Always Update `CONFIG.md`

Update `CONFIG.md` (ENG) whenever:

- config schema changes
- config defaults change
- config behavior or semantics change

Do not finish a config-related task without checking `CONFIG.md` consistency.

### Always Update `DESIGN.md`

Update `DESIGN.md` (ENG) whenever implementation changes affect:

- architecture
- runtime or packaging
- UI layout or behavior
- scanner logic
- validation rules or modes
- save, backup, or restore behavior
- external assumptions or constraints

How to update it:

- keep it aligned with the current code, not historical plans
- remove or rewrite stale statements instead of stacking contradictory notes
- prefer describing behavior and responsibilities over low-level edit history
- keep `README.md` user-facing and keep deep technical detail in `DESIGN.md`

### Always Update `MiHotKeyApp/DOC.md`

Update `MiHotKeyApp/DOC.md` whenever non-config user-facing behavior changes, especially:

- command-line usage
- tray workflows
- diagnostics or troubleshooting steps
- runtime behavior users may observe
- packaging or run/publish workflows relevant to using the app

How to update it:

- keep it focused on user workflows rather than config schema
- put config structure and examples in `CONFIG.md`
- put technical architecture and internal responsibilities in `DESIGN.md`

## Expectations

- Do not leave `CHANGELOG.md`, `DESIGN.md`, or `MiHotKeyApp/DOC.md` stale after a relevant change.
- If a change is too small to mention in `README.md`, it may still need updates in `CHANGELOG.md`, `DESIGN.md`, or `MiHotKeyApp/DOC.md`.
- Before wrapping up implementation work that changes code, runtime behavior, packaging, or build-relevant project files, run:
  - `dotnet build`
  - `dotnet publish -c Release` (if it cannot complete because the app is running, notify the user)
- Do not run `dotnet build` or `dotnet publish` for documentation-only changes when no code or project files were touched.
- If the user asks for a commit, give a brief description of what changed.
- If the prompt begins with `PUBLISH:`, treat it as a requirement to do these tasks after finishing:
  - run `dotnet build`
  - run `dotnet publish -c Release`
  - create a commit with a brief description
