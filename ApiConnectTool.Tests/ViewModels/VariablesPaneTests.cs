using System.Collections.ObjectModel;
using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class VariablesPaneTests
{
    private readonly RequestEnvironment _dev;
    private readonly RequestEnvironment _prod;
    private readonly RequestEnvironment _global;
    private readonly RequestCollection _orders;
    private readonly RequestItem _request;
    private readonly MainWindowViewModel _vm;

    public VariablesPaneTests()
    {
        _dev = new RequestEnvironment { Name = "dev", Variables = new() { new() { Key = "host", InitialValue = "dev.orders.test" } } };
        _prod = new RequestEnvironment { Name = "prod", IsProduction = true, Variables = new() { new() { Key = "host", InitialValue = "orders.test" } } };
        _global = new RequestEnvironment
        {
            Name = "shared",
            Variables = new() { new() { Key = "token", InitialValue = "s3cr3t-token", IsSecret = true }, new() { Key = "region", InitialValue = "eu" } }
        };
        _request = new RequestItem
        {
            Name = "List orders",
            Url = "https://{{host}}/{{version}}/orders?region={{region}}&t={{token}}&tenant={{tenant}}&x={{missing}}"
        };
        _orders = new RequestCollection
        {
            Name = "Orders", Environments = { _dev, _prod }, ActiveEnvironmentId = _dev.Id, Requests = { _request },
            Variables = new() { new() { Key = "version", InitialValue = "v2" } }
        };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection> { _orders },
            Environments = new ObservableCollection<RequestEnvironment> { _global },
            SelectedEnvironmentId = _global.Id,
            GlobalVariables = new() { new() { Key = "tenant", InitialValue = "acme" } }
        };
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        _vm = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }, execution);
    }

    private VariablePaneRow Row(string name) => _vm.VariablesPane.Rows.Single(row => row.Name == name);

    [Fact]
    public void Rows_ShowEachSourceKindAndTheEffectiveValue()
    {
        _vm.OpenRequestCommand.Execute(_request);
        var pane = _vm.VariablesPane;
        Assert.Equal(new[] { "host", "version", "region", "token", "tenant", "missing" }, pane.Rows.Select(row => row.Name));

        Assert.Equal(("dev", "dev.orders.test"), (Row("host").Source, Row("host").Value));
        Assert.Equal(("Collection variables", "v2"), (Row("version").Source, Row("version").Value));
        Assert.Equal(("shared", "eu"), (Row("region").Source, Row("region").Value));
        Assert.Equal(("Globals", "acme"), (Row("tenant").Source, Row("tenant").Value));
        Assert.True(Row("missing").IsUnresolved);
        Assert.Equal("Unresolved", Row("missing").Source);
        Assert.All(pane.Rows.Where(row => row.Name != "missing"), row => Assert.False(row.IsUnresolved));
    }

    [Fact]
    public void Secrets_ShowTheRedactorPlaceholder()
    {
        _vm.OpenRequestCommand.Execute(_request);
        Assert.Equal(SecretRedactor.Placeholder, Row("token").Value);
        Assert.DoesNotContain(_vm.VariablesPane.Rows, row => row.Value.Contains("s3cr3t-token"));
    }

    [Fact]
    public void SwitchingTheCollectionEnvironment_UpdatesTheRowsAndMarksProduction()
    {
        _vm.OpenRequestCommand.Execute(_request);
        Assert.False(Row("host").IsProductionSource);

        _vm.CollectionEnvironmentPicker.SelectedChoice = _vm.CollectionEnvironmentPicker.Choices.Single(choice => choice.Environment == _prod);
        Assert.Equal(("prod", "orders.test"), (Row("host").Source, Row("host").Value));
        Assert.True(Row("host").IsProductionSource);
    }

    // Edits refresh through the inspector's dispatcher-bound observation, so run on the UI thread.
    [Fact]
    public Task DraftTabs_HaveNoCollectionScope() => ApiConnectTool.Tests.Views.HeadlessSession.RunAsync(() =>
    {
        _vm.AddTabCommand.Execute(null);
        _vm.ActiveTab!.RequestUrl = "https://{{host}}/{{version}}?r={{region}}";
        var pane = _vm.VariablesPane;
        Assert.True(Row("host").IsUnresolved);
        Assert.True(Row("version").IsUnresolved);
        Assert.Equal("shared", Row("region").Source);
        Assert.Equal("Unsaved request", pane.CollectionName);
        Assert.Equal("Not saved to a collection", pane.SavedState);
    });

    [Fact]
    public void GeneralSection_DescribesTheActiveRequest()
    {
        _vm.OpenRequestCommand.Execute(_request);
        var pane = _vm.VariablesPane;
        Assert.Equal("List orders", pane.RequestName);
        Assert.Equal("Orders", pane.CollectionName);
        Assert.Equal("(collection root)", pane.FolderName);
        Assert.StartsWith("Inherited", pane.AuthText);
        Assert.Equal("Saved", pane.SavedState);

        _vm.ActiveTab!.RequestUrl = "https://{{host}}/changed";
        Assert.Equal("Unsaved changes", pane.SavedState);
    }

    [Fact]
    public void HiddenPane_HoldsNoRows_AndRefillsWhenShown()
    {
        _vm.OpenRequestCommand.Execute(_request);
        _vm.VariablesPane.ToggleCommand.Execute(null);
        Assert.False(_vm.VariablesPane.IsVisible);
        Assert.Empty(_vm.VariablesPane.Rows);
        _vm.VariablesPane.ToggleCommand.Execute(null);
        Assert.NotEmpty(_vm.VariablesPane.Rows);
    }

    [Fact]
    public void Visibility_PersistsInTheSessionState()
    {
        var store = new RecordingSessionStore();
        var vm = new MainWindowViewModel(new FakeWorkspaceService(), sessionStore: store);
        vm.VariablesPane.IsVisible = false;
        Assert.False(store.Saved!.VariablesPaneVisible);

        var reopened = new MainWindowViewModel(new FakeWorkspaceService(), sessionStore: store);
        Assert.False(reopened.VariablesPane.IsVisible);
    }

    private sealed class RecordingSessionStore : ISessionStore
    {
        public SessionState? Saved { get; private set; }
        public SessionState Load() => Saved ?? SessionState.Empty;
        public void Save(SessionState state) => Saved = state;
    }
}
