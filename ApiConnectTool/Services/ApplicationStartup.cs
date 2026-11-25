using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public static class ApplicationStartup
{
    public static int Run(string dataDirectory, Action<string> start, Action<string> explain)
    {
        var canonical = CanonicalDataDirectory.Resolve(dataDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToUpperInvariant())));
        using var ownership = new Mutex(false, @"Global\ApiConnectTool.DataDirectory." + key);
        bool acquired;
        try { acquired = ownership.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            explain(DefaultStrings.Get("Str.Startup.DataDirectoryInUse"));
            return 0;
        }
        try
        {
            start(canonical);
            return 0;
        }
        finally { ownership.ReleaseMutex(); }
    }

    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiConnectTool");
}
