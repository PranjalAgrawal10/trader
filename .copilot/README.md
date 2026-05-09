Purpose
=======

This directory contains Copilot-facing policy/rule documents that mirror the repository's canonical `.cursor/rules/` guidance. The files are intended to be read by AI assistants (like Copilot) to provide consistent, repo-specific recommendations when generating code or suggesting changes.

If `.cursor/rules/` exists or is updated, keep these files in sync. If you want an automated sync step, let me know and I can add a small script or CI job to compare and copy files.

Included rules
--------------
- `core-principles.mdc` — SOLID mapped to this repo's layers and short architecture constraints.
- `ef-core-migrations.mdc` — EF Core migration guidance (don't rewrite history, where migrations live, commands and CI flags).
- `datetime-display.mdc` — Frontend timestamp display rules (format and timezone rules).
- `frontend-api-patterns.mdc` — Frontend API/retry and same-origin guidance.

How to use
----------
- Copilot and other assistants should prefer these rules over generic recommendations when editing files in this repository.
- Keep the files concise. If you update `.cursor/rules/`, mirror changes here.

