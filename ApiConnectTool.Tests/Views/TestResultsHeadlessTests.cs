using System.Net;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class TestResultsHeadlessTests
{
    [Fact]
    public Task ResultsTab_RendersRowsSummaryAndEmptyState() => HeadlessSession.RunAsync(() =>
    {
        AppLocalization.Apply(AppLocalization.DefaultCulture);
        var tab = new RequestTabViewModel();
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        try
        {
            window.Show();
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = tabs.ItemCount - 1;
            Refresh();
            Assert.Contains(Texts(), text => text == "No test results");
            tab.AssertionTests.Assertions.Add(new() { Description = "success" });
            tab.AssertionTests.Assertions.Add(new() { ExpectedValue = "201", Description = "created" });
            tab.AssertionTests.Evaluate(tab.AssertionTests.CreateSnapshot(), new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
            Refresh();
            Assert.Contains("1 of 2 passed", Texts());
            Assert.Contains("PASS", Texts());
            Assert.Contains("FAIL", Texts());
            Assert.Contains("success", Texts());
            Assert.Contains("created", Texts());
            Assert.Contains("200", Texts());
            Assert.Contains(Texts(), text => text != null && text.Contains("actual value was 200"));
            tab.AssertionTests.Evaluate([], new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
            Refresh();
            Assert.Contains("No test results", Texts());
            Assert.DoesNotContain("PASS", Texts());
            tab.AssertionTests.NotEvaluated();
            Refresh();
            Assert.Contains("Tests were not evaluated", Texts());
        }
        finally { window.Close(); }
        void Refresh() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        string?[] Texts() => view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
    });

    [Fact]
    public Task TruncatedSend_ShowsIncompleteBodyChecksBesideMetadataResults() => HeadlessSession.RunAsync(async () =>
    {
        AppLocalization.Apply(AppLocalization.DefaultCulture);
        // The full body is a capped prefix plus a suffix, so a prefix Equals must not pass.
        var cap = RequestExecutionOptions.DefaultMaxResponseBytes;
        var full = new byte[cap + 6];
        Array.Fill(full, (byte)'a');
        "suffix"u8.CopyTo(full.AsSpan(cap));
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(full) }))));
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test" };
        tab.AssertionTests.Assertions.Add(new() { Kind = AssertionKind.Body, ExpectedValue = new string('a', cap), Description = "prefix" });
        tab.AssertionTests.Assertions.Add(new() { Description = "success" });
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        try
        {
            window.Show();
            await tab.SendCommand.ExecuteAsync(null);
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = tabs.ItemCount - 1;
            Refresh();
            Assert.True(tab.ResponseBodyWasTruncated);
            Assert.Contains("1 of 2 passed, 1 incomplete (response body truncated)", Texts());
            Assert.Contains("INCOMPLETE", Texts());
            Assert.Contains("PASS", Texts());
            Assert.DoesNotContain("FAIL", Texts());
            Assert.Contains(Texts(), text => text != null && text.StartsWith("Not evaluated: the response body was truncated", StringComparison.Ordinal));
        }
        finally { window.Close(); }
        void Refresh() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        string?[] Texts() => view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
    });
}
