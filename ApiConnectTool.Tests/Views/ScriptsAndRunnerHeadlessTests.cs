using System.Net;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ScriptsAndRunnerHeadlessTests
{
    [Fact]
    public Task ScriptEditors_BindAndMarkDirty() => HeadlessSession.RunAsync(() =>
    {
        var vm = RequestTabViewModel.FromRequest(new() { PreRequestScript = "before", TestScript = "after" });
        var control = new RequestEditorControl { DataContext = vm };
        var owner = new Window { Content = control };
        try
        {
            owner.Show();
            var tabs = control.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = tabs.ItemCount - 1;
            owner.UpdateLayout();
            var before = control.FindControl<TextBox>("PreRequestScriptEditor")!;
            var after = control.FindControl<TextBox>("TestScriptEditor")!;
            Assert.Equal("before", before.Text);
            Assert.Equal("after", after.Text);
            before.Text = "edited";
            Assert.Equal("edited", vm.AssertionTests.PreRequestScript);
            Assert.True(vm.IsDirty);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task RunnerWindow_ShowsResultsAndClosingCancels() => HeadlessSession.RunAsync(async () =>
    {
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            if (++count == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            sending.SetResult(); return new TaskCompletionSource<HttpResponseMessage>().Task;
        }));
        var vm = new CollectionRunnerViewModel(CollectionRunnerTests.Collection(3), runner, [], null);
        var window = new CollectionRunnerWindow { DataContext = vm };
        try
        {
            window.Show();
            var running = vm.StartCommand.ExecuteAsync(null);
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(vm.Rows);
            window.Close();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ApiConnectTool.Models.CollectionRunEndState.Cancelled, vm.Report!.EndState);
        }
        finally { window.Close(); }
    });
}
