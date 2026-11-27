---
id: collections
title: Collections and History
order: 3
---

# Collections and History

The navigator on the left has two tabs: **Collections** and **History**.

## Collections

Collections show as a tree. Each collection and folder has an icon, and each request shows its method in a colored column. Click a request, or select it and press Enter, to open it in a tab.

1. Choose **File > New Collection** and give the collection a name.
2. Right-click the collection and choose **Add Folder** to organize requests. A new folder or collection starts with its name selected, ready to type over. Right-click a folder and choose **Add Request** to add a request straight into it. Imported collections can also contain nested folders.
3. Open a request and use **File > Save Request As** to select the destination collection or folder.
4. Right-click a request and choose **Move to...** to move it, or **Move Up** and **Move Down** to change its order.
5. Use **Rename**, **Duplicate** or **Delete** from the right-click menu. Deletion asks for confirmation and removes the contained requests.

Every row has a context menu. Open it by right-clicking the row, or by selecting the row and pressing Shift+F10 or the Menu key. The selected collection is also the target of the **Collection** menu on the menu bar.

- **Collection:** Add Request, Add Folder, Rename, Duplicate, Run Collection, Environments, Collection Settings, Export, Export as Postman, Delete.
- **Folder:** Add Request, Rename, Run Folder, Folder Settings, Delete.
- **Request:** Open, Rename, Duplicate, Move to, Move Up, Move Down, Delete.

With a row selected, F2 renames it in place (Enter keeps the new name, Escape restores the old one) and Delete deletes it after confirmation.

A collection that has environments shows its active environment at the right of its row. A production environment shows a red **PROD** tag and a red name, so you can see each API's stage at a glance. Change it with the environment picker on the toolbar. See [Environments and Variables](environments).

The search box finds requests across collections. **Collection Settings...** edits a collection's variables and authentication; **Folder Settings...** edits inherited authentication.

Choose **Run Collection...** or **Run Folder...** to run the requests in tree order. See [Collection Runner](collection-runner).

## History

The History tab lists recent sends: method, name, status (green for 2xx, blue for 3xx, red for errors) and time. Double-click an entry, or select it and press Enter, to resend it in a new tab. Right-click an entry to resend it, to compare it with another entry, or to compare it with the active tab's response. See [History and comparing responses](history).

Return to [Contents](contents).
