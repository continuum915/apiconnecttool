---
id: requests
title: Requests
order: 2
---

# Requests

1. Choose **File > New Request** (Ctrl+N).
2. Choose an HTTP method beside the URL, then enter the full URL of your API, for example `http://localhost:8080/users`.
3. Add optional query parameters in **Params** and headers in **Headers**. Clear a row's checkbox to exclude it.
4. For a POST or PUT, open **Body**, choose **raw**, enter the body and set its content type, such as `application/json`.
5. Click **Send** (Ctrl+Enter). The response shows its status, elapsed time, size, headers and body. **Cancel** stops an active send.
6. Choose **File > Save Request** (Ctrl+S), select a collection or folder and enter a name.

The URL and **Params** grid stay in sync. Paste a URL with a query string to fill the grid, or add, edit, disable and delete rows to update the URL. Typing a new query replaces the enabled rows and keeps disabled rows. Variables, repeated keys, valueless keys such as `?flag`, already-encoded values and fragments retain their spelling in the editor. A fragment such as `#section` is not a query parameter.

In **Headers**, the read-only notice lists ApiConnectTool's defaults: `User-Agent: ApiConnectTool/<version>`, `Accept: */*` and `Accept-Encoding: gzip, deflate, br`. Add an enabled header with the same name to override it. Names are case-insensitive. ApiConnectTool decodes supported compressed responses before displaying them, and the response size reflects the decoded bytes. Generated cURL leaves these defaults to curl.

## Choose a body

**none** sends no body. **raw** sends the text you enter. **form-data** sends multipart fields: enter each field's name, choose **Text** or **File**, then enter a value or click **Browse...** to select a local file. ApiConnectTool stores the path and reads the file when sending; keep the file available for later sends and runs. **x-www-form-urlencoded** sends only text fields. **binary** sends a selected file as the whole body. GET and HEAD do not send bodies.

## Inspect the response

Choose **Pretty** to indent JSON or XML, or **Raw** to see the original decoded text. JSON number text is preserved. Malformed data and XML containing a DTD are shown as plain text. Syntax highlighting follows the response content type. An invisible-character notice warns about hidden or direction-changing Unicode characters in either view; JSON Pretty escapes those characters.

Click inside the response body and press Ctrl+F to search. **Copy** copies the view currently shown. **Save Response** writes the received bytes; it is disabled when the preview was truncated. **Download** sends the request again and streams the full response to a file. A download can take as long as it needs while data keeps arriving; the request timeout applies to the response headers and then to each pause in the data, and a download that receives nothing for that long fails as stalled. The previous file at the destination is kept until the download completes. Assertions and scripts read the original response, regardless of which view is selected.

## Change request settings

In **Settings**, set the timeout (the limit for a whole Send, or for each pause in a download), proxy URL and TLS certificate policy. **Follow redirects** is beside the URL. These settings are saved with the request and used in collection runs. Enable the certificate exception only for a development endpoint you control. Use [Environments and Variables](environments) for reusable URLs and [Authorization](authorization) for credentials.

Return to [Contents](contents).
