---
id: data-and-secrets
title: Where your data and secrets are stored
order: 11
---

# Where your data and secrets are stored

1. Save your request with **File > Save Request**. ApiConnectTool stores local data under `%LOCALAPPDATA%\ApiConnectTool` for your Windows account.
2. To inspect the files, paste that path into File Explorer's address bar. Close ApiConnectTool before making a backup or changing files.
3. Right-click a collection and choose **Export...** to share requests. Keep secrets excluded unless you intend to share them.

| File | Contents |
|---|---|
| `workspace.json` | Collections with their folders, requests, collection variables and collection environments; global environments, Globals, preferences and the active environment selections. |
| `workspace.json.bak` | Previous workspace version for recovery; it can contain older protected secrets. |
| `history.json` and `history.json.bak` | Up to 500 request snapshots and redacted response previews. |
| `session.json` | Saved request IDs for open tabs and the active tab; unsaved drafts and responses are not restored. |
| `cookies.json` | Shared cookie jar, stored as plain JSON. |
| `oauth-tokens.json` | Cached OAuth access and refresh tokens, protected before storage. |

Workspace and history saves use temporary files and replace the previous file, retaining a backup. If a workspace cannot be read, ApiConnectTool tries its backup and displays a warning. An unreadable workspace can be preserved as `workspace.unreadable-<timestamp>.json`. A failed save remains visible until dismissed or a later save succeeds.

Authentication secret fields and secret variable values use Windows DPAPI for the current user. They are encrypted in the JSON files and backups; ApiConnectTool refuses to save them as plaintext if protection fails. Copying encrypted files to another account or machine can make secrets unreadable. Ordinary request URLs, bodies, headers and nonsecret values are plain data, so protect the whole folder if they contain sensitive information. Cookies are also plaintext.

History and generated output redact resolved secret values, but arbitrary text is not automatically a designated secret. Mark credentials as **Secret** in the variable editor. Revealed cURL output and exports that include secrets contain plaintext credentials. Exports omit history, cookies and cached OAuth tokens. A form-data or binary body stores a local file path; ApiConnectTool reads the file when sending and does not store its contents in the workspace or request history.

Return to [Contents](contents).
