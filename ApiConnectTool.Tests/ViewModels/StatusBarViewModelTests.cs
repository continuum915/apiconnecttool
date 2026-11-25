using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class StatusBarViewModelTests
{
    private readonly RequestEnvironment _dev = new() { Name = "dev" };
    private readonly RequestEnvironment _prod = new() { Name = "prod", IsProduction = true };
    private readonly RequestEnvironment _global = new() { Name = "shared" };
    private readonly RequestItem _request = new() { Name = "List orders", Url = "https://orders.test" };
    private readonly RequestCollection _orders;
    private readonly MainWindowViewModel _vm;
    private readonly ManualScheduler _scheduler = new();
    private readonly StatusBarViewModel _bar;

    public StatusBarViewModelTests()
    {
        _orders = new RequestCollection { Name = "Orders", Environments = { _dev, _prod }, ActiveEnvironmentId = _dev.Id, Requests = { _request } };
        _vm = new MainWindowViewModel(new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace
            {
                Collections = new ObservableCollection<RequestCollection> { _orders },
                Environments = new ObservableCollection<RequestEnvironment> { _global }
            }
        });
        _bar = new StatusBarViewModel(_vm, _scheduler.Schedule);
    }

    [Fact]
    public void CollectionTab_ShowsItsCollectionEnvironmentLayoutProxyAndSaveState()
    {
        _vm.OpenRequestCommand.Execute(_request);
        Assert.Equal("Orders", _bar.CollectionText);
        Assert.Equal("dev", _bar.EnvironmentText);
        Assert.False(_bar.IsProductionEnvironment);
        Assert.Equal("Vertical layout", _bar.LayoutText);
        Assert.Equal("Proxy: None", _bar.ProxyText);
        Assert.Equal("All changes saved", _bar.SaveStateText);

        _vm.ActiveTab!.ProxyUrl = "http://127.0.0.1:8888";
        Assert.Equal("Proxy: 127.0.0.1:8888", _bar.ProxyText);
        _vm.ToggleLayoutCommand.Execute(null);
        Assert.Equal("Horizontal layout", _bar.LayoutText);
    }

    [Fact]
    public void ProductionCollectionEnvironment_IsMarked()
    {
        _vm.OpenRequestCommand.Execute(_request);
        _vm.CollectionEnvironmentPicker.SelectedChoice = _vm.CollectionEnvironmentPicker.Choices.Single(choice => choice.Environment == _prod);
        Assert.True(_bar.IsProductionEnvironment);
        Assert.Equal("prod (production)", _bar.EnvironmentText);
    }

    [Fact]
    public void DraftTab_ShowsUnsavedRequestAndTheGlobalEnvironment()
    {
        _vm.AddTabCommand.Execute(null);
        Assert.Equal("Unsaved request", _bar.CollectionText);
        Assert.Equal("No environment", _bar.EnvironmentText);
        _vm.SelectedEnvironment = _global;
        Assert.Equal("shared", _bar.EnvironmentText);
    }

    [Fact]
    public void SaveError_ShowsTheErrorTitle()
    {
        _vm.WorkspaceSaveError = "disk full";
        Assert.True(_bar.HasSaveError);
        Assert.NotEqual("All changes saved", _bar.SaveStateText);
        _vm.WorkspaceSaveError = null;
        Assert.Equal("All changes saved", _bar.SaveStateText);
    }

    [Fact]
    public void Messages_ShowUntilTheyExpireOrAreReplaced()
    {
        Assert.Equal("Ready", _bar.Message);
        _vm.CollectionsPanel.ImportExportMessage = "Imported orders.json.";
        Assert.Equal("Imported orders.json.", _bar.Message);
        Assert.Equal(StatusBarViewModel.MessageDuration, _scheduler.LastDelay);

        _vm.OpenRequestCommand.Execute(_request);
        _vm.ActiveTab!.SetResponseActionMessage("Response copied.");
        Assert.Equal("Response copied.", _bar.Message);
        Assert.True(_scheduler.Pending[0].Canceled);

        _scheduler.RunAll();
        Assert.Equal("Ready", _bar.Message);
    }

    [Fact]
    public async Task SavingARequest_PostsASavedMessage()
    {
        _vm.OpenRequestCommand.Execute(_request);
        _vm.ActiveTab!.RequestUrl = "https://orders.test/changed";
        Assert.True(_vm.ActiveTab.IsDirty);
        await _vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Saved 'List orders'.", _bar.Message);
    }

    private sealed class ManualScheduler
    {
        public List<Entry> Pending { get; } = new();
        public TimeSpan LastDelay { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            LastDelay = delay;
            var entry = new Entry(action);
            Pending.Add(entry);
            return entry;
        }

        public void RunAll()
        {
            foreach (var entry in Pending.ToArray().Where(entry => !entry.Canceled)) entry.Action();
        }

        public sealed class Entry(Action action) : IDisposable
        {
            public Action Action { get; } = action;
            public bool Canceled { get; private set; }
            public void Dispose() => Canceled = true;
        }
    }
}
