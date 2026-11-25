using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;

namespace ApiConnectTool.Tests.Views;

/// <summary>Entry point for Avalonia's headless platform: the real <see cref="App"/> (themes,
/// design tokens, localized strings) with no window server, so views can be built and inspected
/// in tests without ever touching the desktop.</summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>One headless session for the whole test assembly; every UI test runs its body on
/// that session's UI thread via <see cref="RunAsync"/>.</summary>
public static class HeadlessSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp)));

    public static Task RunAsync(Func<Task> body) =>
        Session.Value.Dispatch(async () => { await body(); return true; }, CancellationToken.None);

    public static Task RunAsync(Action body) =>
        Session.Value.Dispatch(body, CancellationToken.None);
}
