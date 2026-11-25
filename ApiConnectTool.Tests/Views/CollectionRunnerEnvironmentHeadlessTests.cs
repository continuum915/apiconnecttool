using System.Net;
using Avalonia.Controls;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class CollectionRunnerEnvironmentHeadlessTests
{
    [Fact]
    public Task RunnerWindowShowsTheCollectionAndGlobalEnvironmentPickers() => HeadlessSession.RunAsync(() =>
    {
        var dev = new RequestEnvironment { Name = "dev" };
        var qa = new RequestEnvironment { Name = "qa" };
        var global = new RequestEnvironment { Name = "shared" };
        var collection = new RequestCollection { Environments = { dev, qa }, ActiveEnvironmentId = qa.Id };
        var vm = new CollectionRunnerViewModel(collection, new CollectionRunner(RequestRunnerTests.Runner(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))), [global], global);
        var window = new CollectionRunnerWindow { DataContext = vm };
        try
        {
            window.Show(); window.UpdateLayout();
            var collectionPicker = window.FindControl<ComboBox>("CollectionEnvironmentPicker")!;
            var globalPicker = window.FindControl<ComboBox>("GlobalEnvironmentPicker")!;

            Assert.Equal(new[] { "No environment", "dev", "qa" },
                collectionPicker.Items.Cast<EnvironmentChoice>().Select(choice => choice.Name));
            Assert.Same(qa, Assert.IsType<EnvironmentChoice>(collectionPicker.SelectedItem).Environment);
            Assert.Same(global, globalPicker.SelectedItem);
        }
        finally { window.Close(); }
    });
}
