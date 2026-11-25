using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class RequestDefaultsHeadlessTests
{
    [Fact]
    public Task UrlBoxAndParamsBindingsSynchronizeBothDirections() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control };
        try
        {
            window.Show(); window.UpdateLayout();
            var url = control.FindControl<TextBox>("UrlTextBox")!;
            url.Text = "https://example.test/?q=%2F&flag#frag";
            Assert.Equal(new[] { "q", "flag" }, tab.CreateSnapshot().QueryParams.Select(row => row.Key));
            tab.QueryParams[0].Value = "{{value}}";
            Assert.Equal("https://example.test/?q={{value}}&flag#frag", url.Text);
            tab.QueryParams[1].IsEnabled = false;
            Assert.Equal("https://example.test/?q={{value}}#frag", url.Text);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task HeadersTabExplainsDefaultsWithoutAddingEditableRows() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control };
        try
        {
            window.Show();
            var tabs = control.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedItem = control.FindControl<TabItem>("HeadersTab");
            window.UpdateLayout();
            var description = control.FindControl<TextBlock>("DefaultHeadersNotice");
            Assert.NotNull(description);
            Assert.True(description!.IsEffectivelyVisible);
            Assert.Contains("User-Agent: " + DefaultRequestHeaders.UserAgent, description.Text);
            Assert.Contains("Accept: */*", description.Text);
            Assert.Contains("Accept-Encoding: gzip, deflate, br", description.Text);
            Assert.Contains("override", description.Text);
            Assert.Empty(tab.CreateSnapshot().Headers);
        }
        finally { window.Close(); }
    });
}
