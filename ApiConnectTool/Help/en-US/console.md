---
id: console
title: Console
order: 11
---

# Console

The Console records what ApiConnectTool did during this session, across every tab and collection run. Open it with **View > Console**, **Tools > Console** or the toolbar's **Console** button. Only one Console window is open at a time.

## What it records

- **Network:** every send and every request of a collection run, with the method, the resolved URL, the status and the elapsed time. Click the arrow at the start of a network line to show the request headers, the response headers and the first 4 KB of the response body.
- **Scripts:** each `console.log`, `console.info`, `console.warn`, `console.error` and `console.debug` line from pre-request and test scripts, prefixed with the request name and the script (pre-request or test). See [Assertions and Scripts](assertions-and-scripts).
- **Warnings:** variables that were still unresolved when the request was sent, and response bodies larger than the preview limit.
- **Errors:** failed tests and transport errors such as timeouts or refused connections.
- **Info:** when a collection run starts and finishes, with its totals.

Secret values are replaced with a placeholder everywhere in the Console, including URLs, headers, bodies and script output, so you can share your screen or copy lines safely.

## Toolbar

- **Clear** empties the Console.
- **All**, **Network**, **Scripts**, **Warnings** and **Errors** show only that kind of message; each shows how many there are.
- **Timestamps** shows or hides the time of each line.
- **Preserve log** keeps earlier messages when a collection run starts. When it is off, the Console clears at the start of each run.
- The search box filters lines by text.

The status bar shows how many messages are shown, the script output limits, and the number of errors and warnings.

The Console holds at most 2,000 messages; older ones are dropped first. It lives in memory only: nothing is saved, and it is empty again the next time you start ApiConnectTool.

Return to [Contents](contents).
