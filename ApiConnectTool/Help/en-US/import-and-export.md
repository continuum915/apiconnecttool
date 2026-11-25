---
id: import-and-export
title: Import and Export
order: 8
---

# Import and Export

## The import wizard

Choose **File > Import...** or the toolbar's **Import...** button. **File > Import From** opens the wizard with a format already chosen. The wizard has five steps, listed on the left; **Back** and **Next** move between them, and **Cancel** closes the wizard without changing anything.

1. **Format:** choose ApiConnectTool collection, Postman Collection v2.1, OpenAPI 3.x document, or cURL command.
2. **Source:** choose a file with **Browse...**, or select **Paste text** and paste it. The wizard reads it right away and shows what it found (folders, requests, collection environments and collection variables), or the reason it cannot read it.
3. **Options:** choose **New collection** and edit its name if you like, or **Existing collection** to add the import to a collection you already have (see below). For an ApiConnectTool collection that has collection environments, **Import collection environments** brings them along; none of them becomes active.
4. **Review:** every folder, request and collection environment is listed with what will happen to it. Untick anything you do not want; it shows **Skip**, and so does everything inside an unticked folder. Nothing is written until you click **Start**.
5. **Import:** the wizard adds the collection, saves your workspace once, and lists what it created. Click **Done** to close.

## Importing into an existing collection

Choose **Existing collection** on the Options step and pick the collection. The Review step then shows what happens to each item:

- **Folders** match by their path (for example `Admin/Reports`), ignoring case. A matching folder shows **Merge**: its new contents go into the folder you already have. Other folders show **New**.
- **Requests** match by name within the same folder, ignoring case. A matching request shows **Skip**, so your version stays. Tick **Overwrite requests that have the same name** to replace it instead; it then shows **Overwrite** (in amber). Other requests show **New** (in green).
- **Collection variables** match by key, and **collection environments** by name, with the same Skip or Overwrite rule.

An overwritten request keeps its identity, so open tabs, history links and runner selections still point at it. An open tab for an overwritten request reloads with the imported content; if the tab has unsaved changes, you are asked first: **Save** keeps your edits, **Don't Save** loads the import, and **Cancel** leaves the tab as it is. An overwritten environment keeps its identity and stays active if it was. Secret values stay secret. Importing the same file twice changes nothing the second time: every request shows Skip. The finish line counts what was new, merged, overwritten and skipped.

A cURL command becomes a collection with one request, or a new request in the existing collection you choose. Choose the collection environment, and set URLs and credentials for your target API, before sending.

Postman imports preserve folders, variables, authentication inheritance, request bodies and supported settings. File form-data fields keep their `src` path; ApiConnectTool does not copy the file. Path variables such as `:id` are replaced in the URL by their declared values, including `{{variable}}` references; a path variable with no value becomes a `{{name}}` placeholder for you to define. Postman scripts are not executed during import. The import report names scripts and unsupported authentication or settings that were dropped or could not be mapped. A warning means you should inspect and configure the affected request before relying on it; it does not mean the whole collection failed to import.

OpenAPI imports turn operations into requests. Server variables contribute defaults to the base URL. Supported security schemes become authentication placeholders; supply the actual credentials before sending. Review warnings for unsupported security choices. cURL import builds a request from the supplied command without running it.

## Export or share a collection

1. Right-click the collection in the navigator, or select it and open the **Collection** menu.
2. Choose **Export...** for ApiConnectTool format or **Export as Postman...** for Postman v2.1.
3. Keep secrets excluded for a shared export. Only include them deliberately, then treat the resulting file as sensitive plaintext.
4. Select a destination file.

ApiConnectTool format keeps ApiConnectTool-specific settings and assertions. Postman format maps supported fields and scripts; inspect the result when moving to another client. History, cookies and cached OAuth tokens do not travel with collection exports. File fields export paths, not file contents. Import gives collections and requests fresh IDs.

To copy an individual request, open its cURL copy menu and choose the redacted or revealed variant. File fields use `-F "name=@path"`. Variables resolve against the active collection and global environments; revealed output can contain credentials.

Return to [Contents](contents).
