using Avalonia.Controls;
using Avalonia.Threading;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class StatusBarHeadlessTests
{
    [Fact]
    public Task StatusBarRendersTheMessageThenTheFieldsInOrder() => HeadlessSession.RunAsync(() =>
    {
        var vm = new MainWindowViewModel(new FakeWorkspaceService());
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Ready", window.FindControl<TextBlock>("StatusMessage")!.Text);
            var fields = window.FindControl<StackPanel>("StatusFields")!.Children.OfType<Border>()
                .Select(border => ((TextBlock)border.Child!).Name).ToArray();
            Assert.Equal(new[] { "StatusCollection", "StatusEnvironment", "StatusLayout", "StatusProxy", "StatusSaveState" }, fields);
            Assert.Equal("No request open", window.FindControl<TextBlock>("StatusCollection")!.Text);
            Assert.Equal("All changes saved", window.FindControl<TextBlock>("StatusSaveState")!.Text);
        }
        finally { window.Close(); }
    });
}
