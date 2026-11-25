#pragma warning disable CA1416
using System.Net;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public sealed class StorageWarningHeadlessTests
{
    [Theory]
    [InlineData("history", "io")]
    [InlineData("history", "access")]
    [InlineData("history", "protection")]
    [InlineData("cookies", "io")]
    [InlineData("cookies", "access")]
    [InlineData("cookies", "protection")]
    public Task StorageFailure_DoesNotHideHttpResult_AndRetryClearsWarning(string target, string failure) => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolStorageWorkflow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var secrets = new SwitchableStore();
        var history = new HistoryStore(target == "history" ? secrets : new WindowsDpapiSecretStore(), folder);
        var jar = new CookieJarService(folder, target == "cookies" ? secrets : new WindowsDpapiSecretStore());
        jar.Container.Add(new Uri("https://example.test"), new Cookie("session", "private-session"));
        jar.Save();
        using var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("original body") };
            response.Headers.Add("X-Original", "original header");
            return Task.FromResult(response);
        });
        var execution = new RequestExecutionService(new TestHttpClientFactory(handler), cookieJarService: jar);
        var workspace = new FakeWorkspaceService();
        var vm = new MainWindowViewModel(workspace, execution, historyStore: history, cookieJarService: jar);
        vm.AddTabCommand.Execute(null);
        var tab = vm.ActiveTab!;
        tab.RequestUrl = "https://example.test";
        tab.Auth = new RequestAuth { Type = AuthType.Bearer, Token = "private-token" };
        var dirtyBeforeSend = tab.IsDirty;
        var path = Path.Combine(folder, target == "history" ? "history.json.tmp" : "cookies.json");
        FileStream? lockedFile = null;
        if (failure == "io") lockedFile = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (failure == "access")
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.CreateDirectory(path);
        }
        if (failure == "protection") secrets.Refuse = true;
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            await tab.SendCommand.ExecuteAsync(null);
            await history.FlushAsync();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("202 Accepted", tab.ResponseStatus);
            Assert.Equal("original body"u8.ToArray(), tab.ResponseBytes);
            Assert.Contains(tab.ResponseHeaders, h => h.Key == "X-Original" && h.Value == "original header");
            Assert.False(tab.IsSending);
            Assert.True(tab.LastSendSucceeded);
            Assert.Equal(dirtyBeforeSend, tab.IsDirty);
            Assert.Null(vm.WorkspaceSaveError);
            var warning = target == "history" ? tab.HistoryPersistenceWarning : tab.CookiePersistenceWarning;
            Assert.NotNull(warning);
            Assert.DoesNotContain("private", warning);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == warning);
            Assert.Null(target == "history" ? tab.CookiePersistenceWarning : tab.HistoryPersistenceWarning);
            Assert.Single(vm.History.Entries);
            if (target == "cookies")
            {
                tab.ClearCookiesCommand.Execute(null);
                Assert.Empty(jar.Container.GetCookies(new Uri("https://example.test")));
                Assert.NotNull(tab.CookiePersistenceWarning);
            }
            lockedFile?.Dispose();
            if (failure == "access") Directory.Delete(path);
            secrets.Refuse = false;
            await tab.SendCommand.ExecuteAsync(null);
            await history.FlushAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.HistoryPersistenceWarning);
            Assert.Null(tab.CookiePersistenceWarning);
            Assert.Equal(2, new HistoryStore(new WindowsDpapiSecretStore(), folder).GetAll().Count);
            Assert.Equal("202 Accepted", tab.ResponseStatus);
        }
        finally
        {
            lockedFile?.Dispose();
            window.Close();
            Directory.Delete(folder, true);
        }
    });

    [Fact]
    public Task UnreadableStartupStores_ShowSeparateWarningsAndPreserveWorkspaceWarning() => HeadlessSession.RunAsync(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolStartupWarnings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "history.json"), "private-history");
        File.WriteAllText(Path.Combine(folder, "cookies.json"), "private-cookies");
        var history = new HistoryStore(new WindowsDpapiSecretStore(), folder);
        var jar = new CookieJarService(folder);
        var vm = new MainWindowViewModel(new FakeWorkspaceService { LastLoadError = "Workspace recovery warning" }, historyStore: history, cookieJarService: jar);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal("Workspace recovery warning", vm.WorkspaceWarning);
            Assert.NotNull(vm.History.PersistenceWarning);
            Assert.NotNull(vm.CookiePersistenceWarning);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToArray();
            Assert.Contains(vm.History.PersistenceWarning, texts);
            Assert.Contains(vm.CookiePersistenceWarning, texts);
            Assert.Contains(vm.WorkspaceWarning, texts);
            Assert.Equal("private-history", File.ReadAllText(Path.Combine(folder, "history.json")));
            Assert.Equal("private-cookies", File.ReadAllText(Path.Combine(folder, "cookies.json")));
        }
        finally { window.Close(); Directory.Delete(folder, true); }
    });

    private sealed class SwitchableStore : ISecretStore
    {
        public bool Refuse { get; set; }
        private readonly WindowsDpapiSecretStore _store = new();
        public bool TryProtect(string plaintext, out string? payload)
        {
            payload = null;
            return !Refuse && _store.TryProtect(plaintext, out payload);
        }
        public bool TryUnprotect(string payload, out string? plaintext) => _store.TryUnprotect(payload, out plaintext);
    }
}
