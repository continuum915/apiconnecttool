using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using ApiConnectTool.Services.Localization;

using System.Threading;
namespace ApiConnectTool
{
    internal sealed class Program
    {
        private const uint MessageBoxInformation = 0x40;
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => ApplicationStartup.Run(
            ApplicationStartup.DefaultDataDirectory,
            _ => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args),
            message => MessageBox(IntPtr.Zero, message, DefaultStrings.Get("Str.Startup.Title"), MessageBoxInformation));

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        public static IServiceProvider BuildServiceProvider(string? appDataFolder = null)
        {
            var services = new ServiceCollection();
            appDataFolder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiConnectTool");

            services.AddSingleton<ICookieJarService>(sp => new CookieJarService(appDataFolder, sp.GetRequiredService<ISecretStore>()));
            services.AddHttpClient("ApiConnectTool.Http")
                .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(sp => new ResponseDecompressionHandler(new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = true,
                    CookieContainer = sp.GetRequiredService<ICookieJarService>().Container
                }));
            services.AddSingleton<ISecretStore>(_ => OperatingSystem.IsWindows()
                ? new WindowsDpapiSecretStore()
                : OperatingSystem.IsMacOS()
                    ? new MacosKeychainSecretStore()
                    : OperatingSystem.IsLinux()
                        ? new LinuxSecretServiceStore()
                        : new UnsupportedSecretStore());
            services.AddSingleton<IWorkspaceService>(sp => new WorkspaceService(sp.GetRequiredService<ISecretStore>(), appDataFolder));
            services.AddSingleton<IVariableResolver, VariableResolver>();
            services.AddSingleton<IOAuthTokenCache>(sp => new OAuthTokenCache(
                sp.GetRequiredService<ISecretStore>(),
                Path.Combine(appDataFolder, "oauth-tokens.json")));
            services.AddSingleton<IOAuthTokenService, OAuthTokenService>();
            services.AddSingleton<IHistoryStore>(sp => new HistoryStore(
                sp.GetRequiredService<ISecretStore>(),
                appDataFolder));
            services.AddSingleton<ISessionStore>(_ => new SessionStore(appDataFolder));
            services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
            services.AddSingleton<MainWindowViewModel>();

            return services.BuildServiceProvider();
        }
    }
}
