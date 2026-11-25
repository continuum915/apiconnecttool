using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class TestResultsConsoleHeadlessTests
{
    [Fact]
    public Task ConsoleSection_AppearsOnlyWhenTheRunHasConsoleLines() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = tabs.ItemCount - 1;
            window.UpdateLayout();
            var console = view.FindControl<Border>("TestResultsConsole")!;

            tab.AssertionTests.Apply(Run([]));
            Dispatcher.UIThread.RunJobs();
            Assert.False(console.IsVisible);

            tab.AssertionTests.Apply(Run([
                new ScriptConsoleLine(ScriptConsoleLevel.Log, "hello", ScriptPhase.PreRequest),
                new ScriptConsoleLine(ScriptConsoleLevel.Error, "bad thing", ScriptPhase.Test)]));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(console.IsVisible);
            var texts = console.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains("LOG", texts);
            Assert.Contains("hello", texts);
            Assert.Contains("ERROR", texts);
            Assert.Contains("bad thing", texts);
            var level = console.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "ERROR");
            Assert.True(view.TryFindResource("ApiConnectTool.Brush.Danger", window.ActualThemeVariant, out var danger));
            Assert.Same(danger, level.Foreground);

            tab.AssertionTests.NotEvaluated();
            Dispatcher.UIThread.RunJobs();
            Assert.False(console.IsVisible);
        }
        finally { window.Close(); }
    });

    private static RequestRunResult Run(IReadOnlyList<ScriptConsoleLine> lines) =>
        new(new RequestExecutionResult { IsSuccess = true, StatusCode = System.Net.HttpStatusCode.OK }, [], true,
            new Dictionary<string, string>(), new HashSet<string>(), []) { Console = lines };
}
