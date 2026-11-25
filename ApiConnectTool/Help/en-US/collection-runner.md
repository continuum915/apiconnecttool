---
id: collection-runner
title: Collection Runner
order: 7
---

# Collection Runner

1. Right-click the collection and choose **Run Collection...**, or right-click a folder and choose **Run Folder...**. The toolbar's **Run Collection** button and **Tools > Run Collection...** run the active tab's collection.
2. Under **Requests to run**, tick the requests to include. All are ticked at first; **Select all** and **None** change them all at once. The choice applies to this window only and is not saved.
3. Under **Settings**, choose the collection environment and, when global environments exist, the global environment. These choices apply to this run only and do not change the toolbar. Folder runs use the same environments as whole-collection runs.
4. Optionally click **Choose data file...** and select a CSV or JSON file. CSV uses a header row for variable names; JSON uses an array of objects. Each row is a separate iteration. **Clear data** returns to a single iteration.
5. Enter a nonnegative **Delay (ms)** to pause between requests, and optionally select **Stop on first failure**.
6. Click **Start**. The button shows how many requests will run.
7. Click **Cancel**, or close the window, to stop. Earlier results remain visible.

## Reading the results

Above the results, a bar fills with green, red and amber segments for passed, failed and errored results, and counters show **Run**, **Passed**, **Failed**, **Errored** and **Total time**. Each result row shows an outcome icon, the method, the request, the iteration, the status code, the elapsed time, the outcome, the test summary and any error. Failed and errored rows are tinted red.

Select a row to see its details below the results:

- **Test Results:** each test and assertion, with failure messages.
- **Console:** the request's script console output.
- **Request:** the method, the resolved URL and the headers that were sent.
- **Response:** the response headers and the first 16 KB of the body.

Secret values are replaced with a placeholder everywhere in the details. Details are kept only while the runner window is open; nothing is saved.

A folder run includes that folder and all its descendants and names the folder in the window title. Requests run in this order: top-level folders before root requests, with each folder's requests before its child folders. Authentication inheritance, request settings, file fields and the shared cookie jar work as in individual sends. Production confirmation happens once before the run starts.

Iteration values override saved variable scopes. `pm.variables.set` values flow to later requests in the same iteration, then reset for the next iteration. Scripts that use `pm.environment`, `pm.collectionVariables` or `pm.globals` update saved current values when the run finishes; `pm.environment` writes go to the run's collection environment when one is selected, and otherwise to its global environment. See [Assertions and Scripts](assertions-and-scripts).

Return to [Contents](contents).
