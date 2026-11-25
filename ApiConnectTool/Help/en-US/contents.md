---
id: contents
title: Contents
order: 1
---

# ApiConnectTool Help

Welcome to ApiConnectTool, a local-first HTTP client for building, testing, and organizing API requests.

## Topics

- [Requests](requests) — Configure URLs, query parameters, headers, bodies, and per-request settings.
- [Collections and History](collections) — Organize saved requests into collections and folders, and inspect recent sends.
- [Environments and Variables](environments) — Collection and global environments, precedence, globals, and secret values.
- [Authorization](authorization) — Configure Bearer, Basic, API Key, and OAuth 2.0 authentication.
- [Assertions and Scripts](assertions-and-scripts) — Validate responses with declarative assertions, JSON Schema checks, and JavaScript scripts.
- [Collection Runner](collection-runner) — Run an entire collection in sequence with optional CSV or JSON iteration data.
- [Import and Export](import-and-export) — Import and export ApiConnectTool collections, Postman collections, OpenAPI documents, and cURL commands.
- [Preferences](preferences) — Configure workspace-wide preferences and safety prompts.
- [Keyboard Shortcuts](keyboard-shortcuts) — Reference all keyboard shortcuts available in ApiConnectTool.
- [History and comparing responses](history) — Reopen sends and compare response previews.
- [Console](console) — See every send, run, script message, warning and error of this session.
- [Where your data and secrets are stored](data-and-secrets) — Find local files, backups and protected credentials.

## Where commands live

Every command is on the menu bar. Commands that have nothing to act on are disabled.

- **File**: New Request, New Collection, Import (the import wizard), Import From (the wizard with a format chosen), Save Request, Save Request As, Exit.
- **Edit**: Environments (for the active or selected collection), Global Environments, Preferences.
- **View**: Collections, History, History Window, Console, Variables Pane, Toggle Layout, and Theme (Dark, Light, Win7 light; the same setting as Preferences).
- **Collection**: acts on the collection selected in the sidebar, or the collection of the selected folder or request. Add Request, Add Folder, Rename, Duplicate, Run Collection, Run Folder (when a folder is selected), Environments, Collection Settings, Export, Export as Postman, Delete.
- **Request**: acts on the active tab. Send, Cancel, Save, Copy as cURL (redacted), Copy as cURL (reveal secrets), Inspect Variables, Move to, Duplicate, Close Tab. Move to and Duplicate need a saved request.
- **Tools**: Run Collection runs the active tab's collection, or the selected collection when no tab is open. Compare Responses compares the active tab's response with the previous send of the same request. Console opens the [Console](console).
- **Help**: ApiConnectTool Help, [Keyboard Shortcuts](keyboard-shortcuts), About ApiConnectTool.

Each menu and item has an access key: press Alt and the underlined letter.
