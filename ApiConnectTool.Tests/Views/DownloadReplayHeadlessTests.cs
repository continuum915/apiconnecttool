using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class DownloadReplayHeadlessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutationDialogDeclineAndCloseSendNothing(bool close) => HeadlessSession.RunAsync(async () =>
    {
        AppLocalization.Apply(AppLocalization.DefaultCulture);
        var sends = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bytes") });
        })));
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test", Method = "POST" };
        var owner = new Window { Content = new ResponseViewerControl { DataContext = tab } };
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            owner.Show();
            await tab.SendCommand.ExecuteAsync(null);
            var download = tab.DownloadResponseAsync(destination, method => DownloadConfirmationWindow.ShowAsync(owner, method));
            var prompt = Assert.IsType<DownloadConfirmationWindow>(Assert.Single(owner.OwnedWindows));
            Assert.Equal(AppLocalization.Get("Str.Response.DownloadConfirmTitle"), prompt.Title);
            var text = prompt.FindControl<TextBlock>("MessageText")!.Text!;
            Assert.Contains("POST", text);
            Assert.Contains("change server data again", text);
            Assert.Contains("Scripts will not rerun", text);
            var cancel = prompt.FindControl<Button>("CancelButton")!;
            Assert.True(cancel.IsDefault);
            if (close) prompt.Close(); else cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await download);
            Assert.Equal(1, sends);
            Assert.False(tab.IsDownloading);
            Assert.False(File.Exists(destination));

            download = tab.DownloadResponseAsync(destination, method => DownloadConfirmationWindow.ShowAsync(owner, method));
            prompt = Assert.IsType<DownloadConfirmationWindow>(Assert.Single(owner.OwnedWindows));
            prompt.FindControl<Button>("SendButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await download);
            Assert.Equal(2, sends);
        }
        finally { owner.Close(); File.Delete(destination); }
    });

    [Fact]
    public Task ViewerExplainsSecondSendAndSaveKeepsExistingCompleteBytesWithoutSending() => HeadlessSession.RunAsync(async () =>
    {
        AppLocalization.Apply(AppLocalization.DefaultCulture);
        var sends = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(" {\"value\":1} ", System.Text.Encoding.UTF8, "application/json") });
        })));
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test" };
        var viewer = new ResponseViewerControl { DataContext = tab };
        var owner = new Window { Content = viewer };
        try
        {
            owner.Show();
            await tab.SendCommand.ExecuteAsync(null);
            owner.UpdateLayout();
            var download = viewer.FindControl<Button>("DownloadButton")!;
            Assert.Equal(AppLocalization.Get("Str.Response.Download"), download.Content);
            Assert.Contains("another HTTP request", Assert.IsType<string>(ToolTip.GetTip(download)));
            var save = viewer.FindControl<Button>("SaveResponseButton")!;
            Assert.True(save.IsEnabled);
            Assert.Contains("without sending", Assert.IsType<string>(ToolTip.GetTip(save)));
            using var output = new MemoryStream();
            output.Write(new byte[100]);
            output.Position = 0;
            Assert.True(await tab.SaveResponseAsync(output));
            Assert.Equal(" {\"value\":1} "u8.ToArray(), output.ToArray());
            Assert.Equal(1, sends);
            tab.ResponseBodyWasTruncated = true;
            Assert.False(save.IsEnabled);
            using var refused = new MemoryStream();
            Assert.False(await tab.SaveResponseAsync(refused));
            Assert.Empty(refused.ToArray());
            Assert.Equal(1, sends);
        }
        finally { owner.Close(); }
    });
}
