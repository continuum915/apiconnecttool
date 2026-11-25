---
id: authorization
title: Authorization
order: 5
---

# Authorization

1. Open a request's **Authorization** tab.
2. Choose a type: **Bearer Token**, **Basic Auth**, **API Key** or **OAuth 2.0**.
3. Fill in the displayed fields. You can use `{{variables}}` for values that vary by environment.
4. Send the request. Bearer and Basic authentication use the Authorization header. API Key uses **Header Name**, or **Query Param** when the header name is blank.

## Share authentication

1. Right-click a collection, choose **Collection Settings...** and configure authentication there.
2. Leave requests on **Inherit** to use that configuration.
3. To override a group, right-click its folder, choose **Folder Settings...** and set authentication there.

Inheritance searches from the request's nearest folder through its parent folders to the collection. **No Auth** explicitly stops inheritance. **Inherit** on a collection resolves to no authentication. The editor shows the effective source and refreshes when parent settings change. Choose an explicit request type to override inheritance for one request.

## Obtain an OAuth token

Choose **OAuth 2.0**, select the grant and supply the client ID, token URL and requested scopes. **Client Credentials** also uses the client secret and obtains a token when sending. For **Authorization Code (PKCE)**, supply the authorization URL, then click **Authorize** and complete the browser login and callback. Cached tokens can be refreshed when supported. If the server rejects a refresh token, Client Credentials gets a fresh token automatically, and Authorization Code asks you to click **Authorize** again.

To discard a cached token, click **Clear Token**. For **Client Credentials**, **Get New Token** clears the token and requests a fresh one immediately, for example after changing roles or scopes. Tokens are cached separately for each combination of active collection environment and active global environment, and these actions use the same environments as Send. Both actions affect only the token for the current combination; tokens for other environments are kept. Authentication secrets and cached tokens are protected for your Windows account; see [Data and secrets](data-and-secrets).

Return to [Contents](contents).
