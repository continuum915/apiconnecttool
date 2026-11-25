using System;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

public class FolderSettingsViewModel
{
    private readonly Action _save;
    public AuthEditorViewModel AuthEditor { get; }
    public FolderSettingsViewModel(CollectionFolder folder, RequestCollection collection, Action save, IOAuthTokenService? oauth = null,
        Workspace? workspace = null, Func<RequestEnvironment?>? environment = null)
    {
        _save = save;
        AuthEditor = new(folder.Auth, oauth, () => EffectiveAuthResolver.Resolve(new() { FolderId = folder.ParentFolderId }, collection));
        AuthEditor.ObserveParents(collection);
        AuthEditor.ConfigureScopes(workspace, collection, environment);
    }
    public void FlushChanges() { _save(); AuthEditor.Dispose(); }
}
