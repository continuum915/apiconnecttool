using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class ScopeEditorTests
{
    [Fact]
    public void GlobalsAreFixedAndCollectionEditsSave()
    {
        var workspace = new Workspace();
        var saves = 0;
        var editor = new EnvironmentEditorViewModel(workspace, () => saves++);
        Assert.Same(editor.Globals, editor.DisplayEnvironments[0]);
        editor.SelectedEnvironment = editor.Globals;
        Assert.False(editor.CanEditEnvironment);
        editor.DeleteEnvironmentCommand.Execute(editor.Globals);
        Assert.Same(editor.Globals, editor.DisplayEnvironments[0]);
        editor.VariableEditor!.AddVariableCommand.Execute(null);
        Assert.Single(workspace.GlobalVariables);
        var collection = new RequestCollection();
        var settings = new CollectionSettingsViewModel(collection, () => saves++);
        settings.VariableEditor.AddVariableCommand.Execute(null);
        settings.VariableEditor.Variables[0].Key = "baseUrl";
        settings.FlushChanges();
        Assert.Equal("baseUrl", Assert.Single(collection.Variables).Key);
        Assert.True(saves > 0);
    }
}
