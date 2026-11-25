using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CookieJarServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolCookieJarTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SetCookieFromOneSend_IsPresentOnASubsequentSendToTheSameOrigin()
    {
        // Uses a real loopback server (no public endpoint) so a real HttpClientHandler applies
        // Set-Cookie/Cookie header logic against the jar's actual CookieContainer - exactly the
        // shared-jar mechanism CookieJarService exists to provide, not a re-test of .NET's own
        // CookieContainer parsing in isolation.
        var jar = new CookieJarService(_tempDirectory);
        using var handler = new HttpClientHandler { CookieContainer = jar.Container, UseCookies = true, AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var baseUrl = $"http://127.0.0.1:{port}/";

        var firstServe = ServeOneRequestAsync(listener, setCookie: true);
        await client.GetAsync(baseUrl);
        await firstServe;

        string? secondRequestText = null;
        var secondServe = ServeOneRequestAsync(listener, setCookie: false, capturedRequestText: text => secondRequestText = text);
        await client.GetAsync(baseUrl);
        await secondServe;
        listener.Stop();

        Assert.NotNull(secondRequestText);
        Assert.Contains("Cookie: session=abc123", secondRequestText);
    }

    [Fact]
    public void Save_ThenNewInstance_ReloadsPersistedCookies()
    {
        var jar = new CookieJarService(_tempDirectory);
        jar.Container.Add(new Uri("https://api.example.test"), new Cookie("session", "abc123") { Expires = DateTime.UtcNow.AddDays(1) });

        jar.Save();
        Assert.DoesNotContain("abc123", File.ReadAllText(Path.Combine(_tempDirectory, "cookies.json")));
        var reloaded = new CookieJarService(_tempDirectory);

        var cookies = reloaded.Container.GetCookies(new Uri("https://api.example.test/pets"));
        Assert.Single(cookies);
        Assert.Equal("abc123", cookies["session"]!.Value);
    }

    [Fact]
    public void Clear_RemovesAllCookiesAndPersistsTheRemoval()
    {
        var jar = new CookieJarService(_tempDirectory);
        jar.Container.Add(new Uri("https://api.example.test"), new Cookie("session", "abc123") { Expires = DateTime.UtcNow.AddDays(1) });
        jar.Save();

        jar.Clear();

        Assert.Empty(jar.Container.GetCookies(new Uri("https://api.example.test")));
        Assert.DoesNotContain("abc123", File.ReadAllText(Path.Combine(_tempDirectory, "cookies.json")));
        var reloaded = new CookieJarService(_tempDirectory);
        Assert.Empty(reloaded.Container.GetCookies(new Uri("https://api.example.test")));
    }

    [Fact]
    public void Constructor_NoFileYet_StartsWithEmptyContainer()
    {
        var jar = new CookieJarService(_tempDirectory);

        Assert.Empty(jar.Container.GetCookies(new Uri("https://api.example.test")));
    }

    [Fact]
    public async Task RedirectAndMultipleIssuedCookies_AreProtectedAndSentAfterReopen()
    {
        var jar = new CookieJarService(_tempDirectory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        using (var handler = new HttpClientHandler { CookieContainer = jar.Container, UseCookies = true })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) })
        {
            var serve = Task.Run(async () =>
            {
                await ServeOneRequestAsync(listener, false, extraHeaders: "Location: /final\r\nSet-Cookie: hop=redirect-secret; Path=/\r\n", status: "302 Found");
                await ServeOneRequestAsync(listener, false, extraHeaders: "Set-Cookie: first=first-secret; Path=/\r\nSet-Cookie: second=second-secret; Path=/\r\n");
            });
            using var response = await client.GetAsync(uri);
            await serve.WaitAsync(TimeSpan.FromSeconds(5));
        }
        jar.Save();
        var persisted = File.ReadAllText(Path.Combine(_tempDirectory, "cookies.json"));
        foreach (var secret in new[] { "redirect-secret", "first-secret", "second-secret" })
        {
            Assert.DoesNotContain(secret, persisted);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), persisted);
        }
        var reopened = new CookieJarService(_tempDirectory);
        using var reopenedHandler = new HttpClientHandler { CookieContainer = reopened.Container, UseCookies = true };
        using var reopenedClient = new HttpClient(reopenedHandler) { Timeout = TimeSpan.FromSeconds(5) };
        string? outgoing = null;
        var capture = ServeOneRequestAsync(listener, false, text => outgoing = text);
        using var finalResponse = await reopenedClient.GetAsync(uri);
        await capture.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("hop=redirect-secret", outgoing);
        Assert.Contains("first=first-secret", outgoing);
        Assert.Contains("second=second-secret", outgoing);
    }
    private static async Task ServeOneRequestAsync(TcpListener listener, bool setCookie, Action<string>? capturedRequestText = null, string extraHeaders = "", string status = "200 OK")
    {
        using var tcpClient = await listener.AcceptTcpClientAsync();
        using var stream = tcpClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLines = new System.Collections.Generic.List<string>();
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            requestLines.Add(line);
        }
        capturedRequestText?.Invoke(string.Join("\n", requestLines));

        var setCookieHeader = setCookie ? "Set-Cookie: session=abc123; Path=/\r\n" : "";
        var body = "ok"u8.ToArray();
        var responseText = $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\n{setCookieHeader}{extraHeaders}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(responseText));
        await stream.WriteAsync(body);
    }
}
