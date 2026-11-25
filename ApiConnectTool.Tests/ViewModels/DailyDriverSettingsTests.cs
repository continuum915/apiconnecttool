using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class DailyDriverSettingsTests
{
    [Fact]
    public async Task SaveSaveAsAndSidebarDuplicateCarrySettings()
    {
        var service = new FakeWorkspaceService();
        var main = new MainWindowViewModel(service);
        main.AddTabCommand.Execute(null);
        var tab = main.ActiveTab!;
        tab.TimeoutSeconds = 85; tab.FollowRedirects = false;
        tab.ProxyUrl = "http://localhost:8888"; tab.IgnoreCertificateErrors = true;
        await main.SaveCommand.ExecuteAsync(null);
        await main.SaveAsCommand.ExecuteAsync(null);
        var collection = Assert.Single(service.LastSaved!.Collections);
        await main.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(collection.Requests[0]);
        Assert.Equal(3, collection.Requests.Count);
        foreach (var saved in collection.Requests)
        {
            var reopened = RequestTabViewModel.FromRequest(saved);
            Assert.Equal(85, reopened.TimeoutSeconds); Assert.False(reopened.FollowRedirects);
            Assert.Equal("http://localhost:8888", reopened.ProxyUrl); Assert.True(reopened.IgnoreCertificateErrors);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidTimeoutIsVisible(int timeout)
    {
        var execution = new ApiConnectTool.Services.RequestExecutionService(new ApiConnectTool.Tests.Services.TestHttpClientFactory(
            new ApiConnectTool.Tests.Services.FakeHttpMessageHandler(_ => throw new Exception("Must not send"))));
        var tab = new RequestTabViewModel(execution) { TimeoutSeconds = timeout };
        await tab.SendCommand.ExecuteAsync(null);
        Assert.False(tab.LastSendSucceeded);
        Assert.Contains("Timeout must be greater than zero", tab.ResponseBody);
    }
    [Fact]
    public void SaveReopenAndDuplicatePreserveSettings()
    {
        var tab = new RequestTabViewModel { TimeoutSeconds = 75, FollowRedirects = false,
            ProxyUrl = "http://localhost:8888", IgnoreCertificateErrors = true };
        var reopened = RequestTabViewModel.FromRequest(tab.CreateSnapshot().Clone());
        Assert.Equal(75, reopened.TimeoutSeconds);
        Assert.False(reopened.FollowRedirects);
        Assert.Equal("http://localhost:8888", reopened.ProxyUrl);
        Assert.True(reopened.IgnoreCertificateErrors);
        Assert.False(reopened.IsDirty);
        reopened.TimeoutSeconds = 60;
        Assert.True(reopened.IsDirty);
    }
}
