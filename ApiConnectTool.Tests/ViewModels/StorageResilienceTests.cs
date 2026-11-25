using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public sealed class StorageResilienceTests
{
    [Fact]
    public async Task Send_PublishesResponseBeforeBlockedHistoryWriteIsReleased()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolHistoryBackground_" + Guid.NewGuid().ToString("N"));
        var secrets = new HistoryStoreTests.GatedSecretStore("private-token");
        var history = new HistoryStore(secrets, folder);
        var vm = new MainWindowViewModel(new FakeWorkspaceService(), new SuccessfulExecution(), historyStore: history);
        vm.AddTabCommand.Execute(null);
        var tab = vm.ActiveTab!;
        tab.RequestUrl = "https://example.test";
        tab.Auth = new RequestAuth { Type = AuthType.Bearer, Token = "private-token" };
        try
        {
            await tab.SendCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(secrets.Entered.Wait(TimeSpan.FromSeconds(5)), "The history write never started.");
            Assert.Equal("200 OK", tab.ResponseStatus);
            Assert.Equal("successful body"u8.ToArray(), tab.ResponseBytes);
            Assert.Contains(tab.ResponseHeaders, h => h.Key == "X-Test" && h.Value == "original");
            Assert.False(tab.IsSending);
            Assert.Equal("https://example.test", Assert.Single(vm.History.Entries).RequestSnapshot.Url);
            Assert.Null(tab.HistoryPersistenceWarning);
            Assert.False(File.Exists(Path.Combine(folder, "history.json")));

            secrets.Release();
            await vm.History.FlushAsync(TimeSpan.FromSeconds(5));

            Assert.Single(new HistoryStore(new HistoryStoreTests.GatedSecretStore("unused"), folder).GetAll());
        }
        finally
        {
            secrets.Release();
            await history.FlushAsync();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task HistoryFlushForExit_GivesUpAfterTimeoutWhileWriteIsBlocked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolHistoryExit_" + Guid.NewGuid().ToString("N"));
        var secrets = new HistoryStoreTests.GatedSecretStore("private-token");
        var history = new HistoryStore(secrets, folder);
        var vm = new HistoryViewModel(history);
        try
        {
            history.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Auth = new RequestAuth { Token = "private-token" } } });
            Assert.True(secrets.Entered.Wait(TimeSpan.FromSeconds(5)));

            await vm.FlushAsync(TimeSpan.FromMilliseconds(50)).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(Path.Combine(folder, "history.json")));
        }
        finally
        {
            secrets.Release();
            await history.FlushAsync();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private sealed class SuccessfulExecution : IRequestExecutionService
    {
        public Task<RequestExecutionResult> ExecuteAsync(RequestItem request, RequestExecutionOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestExecutionResult { IsSuccess = true, StatusCode = HttpStatusCode.OK, ReasonPhrase = "OK", Body = "successful body"u8.ToArray(), ContentType = "text/plain", Headers = new[] { new KeyValueItem { Key = "X-Test", Value = "original" } }, FinalUri = new Uri(request.Url) });
        public Task<RequestDownloadResult> DownloadAsync(RequestItem request, string destinationPath, RequestExecutionOptions? options = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
