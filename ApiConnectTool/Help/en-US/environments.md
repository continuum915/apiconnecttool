---
id: environments
title: Environments and Variables
order: 4
---

# Environments and Variables

Variables let one request work against several targets. Write `{{baseUrl}}/users` in a request URL, and the value of `baseUrl` comes from whichever scope defines it. The same syntax works in headers, query parameters, authentication and bodies.

## Collection and global environments

There are two kinds of environment:

- A **collection environment** belongs to one collection, for example dev, qa and prod for the orders API. Each collection remembers its own active environment, so switching the orders API to qa leaves every other collection unchanged.
- A **global environment** belongs to the workspace and applies to every collection, for example a shared identity provider per stage. The workspace has one active global environment.

Two more scopes hold values that do not change by stage: **collection variables** (defaults for one collection) and **Globals** (defaults for every collection).

A request saved in a collection uses that collection's active environment on top of the active global environment. A request that is not saved in a collection uses only the global environment. A request moved to another collection uses the new collection's environments from then on.

## Choose the active environments

The toolbar has two pickers:

- **Environment for <collection>** applies to the collection of the active tab. It lists that collection's environments and **No environment**. **Manage <collection> environments...** opens the environment editor for the collection. Press **Ctrl+Shift+E** to open this picker from the keyboard. For a request that is not saved in a collection, the picker is disabled and its tooltip explains why.
- **Global environment** sets the workspace's active global environment. It appears once at least one global environment exists.

**Use '<name>' in every collection** in the collection picker makes the environment with that name active in every collection that has one. Names match without regard to case. Collections without an environment of that name keep their current selection, and a banner lists them, so you can see which APIs are not configured for that stage.

Each collection row in the navigator shows its active environment. Production environments are marked **PROD** in the pickers, the navigator and the environment editor.

## Precedence

When names overlap, precedence from highest to lowest is:

1. Run variables, including iteration data and `pm.variables` writes.
2. The active collection environment.
3. The active global environment.
4. Collection variables of the request's collection.
5. Globals.

Within one scope, an override in use beats the default, the last enabled definition wins, and disabled or keyless rows never win. Names are case-sensitive.

Global environments rank above collection variables on purpose. An OpenAPI import writes a `baseUrl` collection variable as a default, and an environment is expected to replace it for each stage. To make one API differ from a shared stage value, define the value in that collection's environment, which ranks above the global environment.

## Structure a multi-API workspace

For several APIs that each run in several stages:

- Put **stage values for one API** (its base URL, tenant ID or API key per stage) in that collection's environments. Give the environments the same names in every collection, such as dev, qa and prod, so **Use '<name>' in every collection** switches the whole workspace in one step.
- Put **stage values shared by several APIs** (a common identity provider or proxy per stage) in global environments.
- Put **values that do not change by stage** in collection variables, or in Globals when every collection uses them.

A large global environment that holds values for several APIs can be split up one API at a time with **Copy to Collection...** or **Move to Collection...** (see below).

## The environment editor

Open the editor for a collection from the collection picker (**Manage <collection> environments...**), the **Collection** menu, or a collection's right-click menu (**Environments...**). **Edit > Environments...** (Ctrl+E) opens it for the active tab's collection, or the selected collection; **Edit > Global Environments...** opens the global environments. The window title names the scope.

- **Toolbar:** the **Scope** selector switches between each collection and **Global environments**. **Add Environment**, **Duplicate...** and **Delete...** act on the selected environment, and the search box on the right filters the list by name. In the global scope, **Copy to Collection...** and **Move to Collection...** move an environment into a collection.
- **List:** a collection scope starts with **Collection variables** (the same values as **Collection Settings > Variables**), then the collection's environments. The global scope starts with **Globals**, then the global environments. The active environment is marked **Active**, and production environments are marked **PROD**.
- **Details:** the environment's name and the **Production environment** check box, then **Add Variable**, **Reset all overrides...**, **Show secret values** and the variables grid: Enabled, Key, Default value, Override value, Use override, Reset, Secret and Remove. An override in use is highlighted, and the default it replaces is dimmed.
- **Footer:** shows the scope's active environment. **Set as Active** makes the selected environment the one requests in that scope use; it is not available for Collection variables or Globals.

Collection and global environments are edited the same way: duplicate, search, reset and delete confirmation work identically in both scopes. Deleting the active environment clears the selection, so nothing keeps sending against a deleted target.

To get started, select a collection in **Scope**, click **Add Environment**, enter a name, click **Add Variable**, then enter a key such as `baseUrl` and a **Default value** such as `http://localhost:8080`. Click **Set as Active**, or choose it in the toolbar's collection picker.

**Override value** takes precedence over **Default value** when **Use override** is checked, including empty overrides. Editing an override enables it. Uncheck **Use override** or choose **Reset override** to clear it without deleting the variable or default. Reset is disabled when no override exists. Overrides persist locally across restarts. Defaults are saved locally and are not automatically team-shared. Disabled variables are excluded.

To edit collection variables, select **Collection variables** in the editor, or right-click the collection in the navigator and choose **Collection Settings...**, then **Variables**. Select **Globals** in the global scope to edit Globals.

Check **Secret** before storing a token. The editor masks secret values; select **Show secret values** only when you need to see them. Secret variables are encrypted on disk and their resolved values are redacted from generated cURL, history previews and results. See [Data and secrets](data-and-secrets).

## Copy or move a global environment to a collection

In the global scope, select an environment and choose **Copy to Collection...** or **Move to Collection...**, then pick the collection. The copy gets a new identity and is not made active. The options for secrets and local overrides are the same as for **Duplicate...**, so secret values are never exposed or silently dropped. **Move to Collection...** asks before it deletes the global original.

## Production environments

Check **Production environment (shows a warning before sending)** for production targets. This works for both kinds of environment. A send asks for confirmation when either the active collection environment or the active global environment is marked production, and the dialog names each one; a collection environment is named with its collection. If both are production, one confirmation covers both.

**Edit > Preferences...** controls when the confirmation appears. With **Warn once per session**, each production environment is confirmed separately: confirming one does not silence the warning for another. A collection run asks once, before any request is sent. See [Preferences](preferences).

## Scripts and environments

In scripts, `pm.environment.get(name)` reads the active collection environment first, then the active global environment, which are the values requests use. `pm.environment.set` and `pm.environment.unset` write to the active collection environment when one is selected, and otherwise to the active global environment. With neither selected, the write fails and names the unavailable scope. `pm.collectionVariables` and `pm.globals` work as before. Script writes set persisted overrides; `unset` removes the variable. See [Assertions and Scripts](assertions-and-scripts).

## OAuth tokens

Cached OAuth tokens are kept separately for each combination of active collection environment and active global environment, so a token obtained against dev is never reused against prod. **Authorize**, **Clear Token** and **Get New Token** use the same environments as Send. See [Authorization](authorization).

## Inspect the current request

The **Variables** pane on the right (View > Variables Pane, or the toolbar's **Variables** button) lists every variable the active request uses, its effective value and where it comes from. Secret values always show as a placeholder there. Choose **Inspect...** in the pane, or **Request > Inspect Variables...**, to inspect a saved-scope preview before sending. It includes current unsaved request edits and effective inherited authorization, and lists each unique supported `{{name}}` reference. The preview covers URL, query, headers, body and content type, form fields and form file paths, and the authorization fields supported by request resolution. Binary body file paths are not variable templates.

Each entry shows whether it resolves, its effective value, its named source scope, and **Default** or **Override**. **(empty)** is a resolved empty string; **Unresolved** means no enabled definition exists for that exact name. **Unavailable (decryption failed)** distinguishes a credential that could not be decrypted from an intentionally empty value. Definitions below each entry show winners, shadowed values and disabled rows, including both default and override details. Names are case-sensitive. Within one scope, the last enabled definition wins; disabled and keyless definitions never win. An unsaved request has no implicit owning collection.

The inspector stays open and refreshes when you switch requests or active environments, edit request values or authorization, change saved variables, or execute scripts that write saved scopes. **Edit defining scope** opens the winning environment, Globals or owning collection settings. For unresolved names, use **Edit current environment** or **Edit Globals**; navigation never creates a variable. Selecting a scope in the editor does not activate it for sending. Use the toolbar environment picker to change the sending environment. Edits use the normal workspace save and retry behavior.

Secret values and all their definition details are masked, including when an enabled secret definition is shadowed by another scope. **Temporarily reveal secrets** reveals them only for the current preview. Closing the preview or refreshing its request, environment or scope context clears reveal. Use Tab to reach scope actions, reveal and Close, Space to activate buttons or toggle reveal, and Escape to close. Inspection neither copies values nor changes the workspace or production confirmation policy.

This is a saved-scope preview, not a prediction of script execution. Pre-request scripts and runner data can override the displayed values. The inspector does not execute scripts or create run or iteration variables. See [Assertions and Scripts](assertions-and-scripts).

Return to [Contents](contents).
