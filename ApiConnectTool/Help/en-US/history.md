---
id: history
title: History and comparing responses
order: 10
---

# History and comparing responses

The navigator's **History** tab lists recent sends. Double-click an entry, or select it and press Enter, to open the captured request in a new tab. Right-click an entry to resend it or compare it.

## The History window

Choose **View > History Window**, the toolbar's **History** button, or **Open History window** at the bottom of the History tab. Only one History window is open at a time.

- **Date groups** on the left (All sends, Today, Yesterday, Earlier this week, Older) show how many sends each holds; select one to list only those sends. Groups follow your local calendar, and weeks start on Monday.
- **The grid** shows the time, method, URL as saved (with `{{variables}}` highlighted), status, duration, response size and collection. A send from a request that was never saved, or has since been deleted, shows **Unsaved request**. Search by request name or URL with the box at the top right.
- **Request and Response** panes below the grid describe the selected send. The URL is shown as saved; resending resolves its variables with the environments active at that time. The stored response body is a redacted preview of up to 16 KB.

Toolbar commands:

1. **Resend in New Tab** opens the captured request in a new tab. Review it, then click **Send**.
2. **Compare...** starts a two-step pick: the button stays pressed until you select a second send, then the comparison opens. Press Escape or click **Compare...** again to cancel.
3. **Compare with Current** compares the selected send with the active tab's response.
4. **Clear History...** deletes every entry after you confirm. This cannot be undone.

The status bar shows the number of entries and errors. History keeps the newest 500 sends.

Comparisons show response status, headers and line differences in body previews. History stores a redacted response preview of up to 16 KB, so a comparison can be incomplete for large responses. A binary response is described rather than stored as text. History records successful and failed individual sends. Request snapshots retain form-data file paths and never embed file contents.

History is stored separately from collections and is not included in collection exports. See [Data and secrets](data-and-secrets).

Return to [Contents](contents).
