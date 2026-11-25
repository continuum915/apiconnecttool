using System.Net.Http;
using System.Reflection;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public static class DefaultRequestHeaders
{
    public static string UserAgent { get; } = "ApiConnectTool/" + typeof(DefaultRequestHeaders).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static string Description => string.Format(AppLocalization.Get("Str.Request.DefaultHeaders"), UserAgent);

    public static void Apply(HttpRequestMessage request)
    {
        Add("User-Agent", UserAgent);
        Add("Accept", "*/*");
        Add("Accept-Encoding", "gzip, deflate, br");
        void Add(string name, string value)
        {
            if (!request.Headers.Contains(name)) request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
