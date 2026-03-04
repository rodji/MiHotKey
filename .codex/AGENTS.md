## Coding principles

- Follow **Single Responsibility Principle**.
- Do not put unrelated functionality into one file/class/method.
- Prefer small focused abstractions over large "god" files.

## Documentation and changelog

- Keep `CHANGELOG.md` in the repository root up to date for every meaningful change.
- Changelog format:
  - Header: `# Changelog`
  - Entry header: `## YYYY-MM-DD {Short description}`
  - Entry body must include:
    - `Intention/Task` (why the change was made)
    - `Changed` (what exactly was changed)

- Keep `CONFIG.md` up to date when:
  - config schema changes,
  - config defaults change,
  - config behavior/semantics change.

- Do not finish a config-related task without checking `CONFIG.md` consistency.
