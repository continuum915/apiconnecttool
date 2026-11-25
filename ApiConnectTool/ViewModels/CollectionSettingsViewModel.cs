using System;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

public class CollectionSettingsViewModel
{
    private readonly Action _save;
    public string Name { get; }
    public VariableEditorViewModel VariableEditor { get; }
    public AuthEditorViewModel AuthEditor { get; }
    public CollectionSettingsViewModel(RequestCollection collection, Action save, IOAuthTokenService? oauth = null,
        Workspace? workspace = null, Func<RequestEnvironment?>? environment = null)
    {
        Name = collection.Name;
        _save = save;
        VariableEditor = new(collection.Variables, save, () => collection.Name);
        AuthEditor = new(collection.Auth, oauth, allowInheritance: false);
        AuthEditor.ConfigureScopes(workspace, collection, environment);
    }
    public void FlushChanges() { _save(); AuthEditor.Dispose(); VariableEditor.Dispose(); }
}
