using System.Net;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ResponseViewerLayoutHeadlessTests
{
    [Theory]
    [InlineData(201, "201 Created", "ApiConnectTool.Brush.SuccessBackground", "ApiConnectTool.Brush.SuccessBorder", "ApiConnectTool.Brush.Success")]
    [InlineData(302, "302 Found", "ApiConnectTool.Brush.Pane", "ApiConnectTool.Brush.Info", "ApiConnectTool.Brush.Info")]
    [InlineData(404, "404 Not Found", "ApiConnectTool.Brush.DangerBackground", "ApiConnectTool.Brush.DangerBorder", "ApiConnectTool.Brush.Danger")]
    [InlineData(500, "500 Internal Server Error", "ApiConnectTool.Brush.DangerBackground", "ApiConnectTool.Brush.DangerBorder", "ApiConnectTool.Brush.Danger")]
    [InlineData(0, "Timed out", "ApiConnectTool.Brush.DangerBackground", "ApiConnectTool.Brush.DangerBorder", "ApiConnectTool.Brush.Danger")]
    public Task StatusChip_IsColoredByStatusClass(int code, string text, string background, string border, string foreground) =>
        HeadlessSession.RunAsync(() =>
        {
            var tab = new RequestTabViewModel
            {
                ResponseStatus = text,
                ResponseStatusClass = code == 0 ? ResponseStatusClass.TransportError : ResponseStatusClassifier.FromStatusCode(code)
            };
            var view = new ResponseViewerControl { DataContext = tab };
            var window = new Window { Content = view, Width = 1100, Height = 500 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var chip = view.FindControl<Border>("ResponseStatusChip")!;
                var label = view.FindControl<TextBlock>("ResponseStatusText")!;
                Assert.True(chip.IsVisible);
                Assert.Equal(text, label.Text);
                Assert.Same(Token(view, window, background), chip.Background);
                Assert.Same(Token(view, window, border), chip.BorderBrush);
                Assert.Same(Token(view, window, foreground), label.Foreground);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task StatusChip_IsHiddenBeforeTheFirstSend() => HeadlessSession.RunAsync(() =>
    {
        var view = new ResponseViewerControl { DataContext = new RequestTabViewModel() };
        var window = new Window { Content = view, Width = 900, Height = 400 };
        try
        {
            window.Show();
            Assert.False(view.FindControl<Border>("ResponseStatusChip")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TabHeadersShowCountsAndResultsAreColored() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view, Width = 1100, Height = 500 };
        try
        {
            window.Show();
            tab.ResponseHeaders.Add(new KeyValueItem { Key = "Content-Type", Value = "application/json" });
            tab.ResponseHeaders.Add(new KeyValueItem { Key = "Date", Value = "today" });
            tab.AssertionTests.Assertions.Add(new() { Description = "ok" });
            tab.AssertionTests.Assertions.Add(new() { ExpectedValue = "201", Description = "created" });
            tab.AssertionTests.Evaluate(tab.AssertionTests.CreateSnapshot(), new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Headers (2)", view.FindControl<TabItem>("ResponseHeadersTab")!.Header);
            Assert.Equal("Cookies (0)", view.FindControl<TabItem>("ResponseCookiesTab")!.Header);
            Assert.Equal("Test Results (1/2)", view.FindControl<TabItem>("TestResultsTab")!.Header);
            Assert.Equal(TestRunOutcome.Failed, tab.Response.TestOutcome);

            view.FindControl<TabControl>("ResponseTabs")!.SelectedItem = view.FindControl<TabItem>("TestResultsTab");
            window.UpdateLayout();
            Assert.Same(Token(view, window, "ApiConnectTool.Brush.Danger"), view.FindControl<TextBlock>("TestResultsSummary")!.Foreground);
        }
        finally { window.Close(); }
    });

    private static IBrush Token(Control view, Window window, string key)
    {
        Assert.True(view.TryFindResource(key, window.ActualThemeVariant, out var resource), key);
        return Assert.IsAssignableFrom<IBrush>(resource);
    }
}
