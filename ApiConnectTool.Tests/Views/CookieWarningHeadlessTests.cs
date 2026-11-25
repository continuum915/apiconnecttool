using System.Net;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public sealed class CookieWarningHeadlessTests
{
    [Fact]
    public Task FailedProtection_ShowsWarningSeparatelyFromSuccessfulResponseAndClear() => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolCookieWarning_" + Guid.NewGuid().ToString("N"));
        var jar = new CookieJarService(folder, new UnsupportedSecretStore());
        jar.Container.Add(new Uri("https://example.test"), new Cookie("session", "private-session"));
        var tab = new RequestTabViewModel(new SuccessfulExecution(), null, null, null, null, jar) { RequestUrl = "https://example.test" };
        var view = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            await tab.SendCommand.ExecuteAsync(null);
            window.UpdateLayout();
            Assert.Equal("200 OK", tab.ResponseStatus);
            Assert.Contains("successful body", tab.ResponseBody);
            Assert.False(tab.IsSending);
            Assert.NotNull(tab.CookiePersistenceWarning);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsVisible && block.Text == jar.PersistenceWarning);
            Assert.DoesNotContain("private-session", tab.CookiePersistenceWarning);
            tab.ClearCookiesCommand.Execute(null);
            Assert.Empty(jar.Container.GetCookies(new Uri("https://example.test")));
            Assert.Equal(jar.PersistenceWarning, tab.CookiePersistenceWarning);
            Assert.False(File.Exists(Path.Combine(folder, "cookies.json")));
        }
        finally { window.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public Task UnreadableStartupJar_ShowsRedactedWarning() => HeadlessSession.RunAsync(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolCookieStartup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "cookies.json");
        File.WriteAllText(file, "private-broken-file");
        try
        {
            var jar = new CookieJarService(folder);
            var vm = new MainWindowViewModel(new FakeWorkspaceService(), cookieJarService: jar);
            Assert.Equal(jar.PersistenceWarning, vm.CookiePersistenceWarning);
            Assert.NotNull(vm.CookiePersistenceWarning);
            Assert.DoesNotContain("private-broken-file", vm.CookiePersistenceWarning);
            Assert.Equal("private-broken-file", File.ReadAllText(file));
        }
        finally { Directory.Delete(folder, true); }
    });

    private sealed class SuccessfulExecution : IRequestExecutionService
    {
        public Task<RequestExecutionResult> ExecuteAsync(RequestItem request, RequestExecutionOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestExecutionResult { IsSuccess = true, StatusCode = HttpStatusCode.OK, ReasonPhrase = "OK", Body = "successful body"u8.ToArray(), ContentType = "text/plain", FinalUri = new Uri(request.Url) });
        public Task<RequestDownloadResult> DownloadAsync(RequestItem request, string destinationPath, RequestExecutionOptions? options = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
