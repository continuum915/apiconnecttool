using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ResponseDecompressionTests
{
    [Theory]
    [InlineData("gzip", "pooled", false)]
    [InlineData("deflate", "pooled", false)]
    [InlineData("br", "pooled", false)]
    [InlineData("gzip", "tls", false)]
    [InlineData("deflate", "tls", false)]
    [InlineData("br", "tls", false)]
    [InlineData("gzip", "proxy", false)]
    [InlineData("deflate", "proxy", false)]
    [InlineData("br", "proxy", false)]
    [InlineData("gzip", "pooled", true)]
    [InlineData("deflate", "tls", true)]
    [InlineData("br", "proxy", true)]
    [InlineData("deflate-raw", "pooled", false)]
    [InlineData("deflate-raw", "tls", true)]
    public async Task TransportDecodesResponseAndPreservesExplicitHeaders(string encoding, string route, bool overrideHeaders)
    {
        var text = new string('x', 2048);
        var bytes = Encoding.UTF8.GetBytes(text);
        using var encoded = new MemoryStream();
        using (Stream compressor = encoding switch {
            "gzip" => new GZipStream(encoded, CompressionMode.Compress, true),
            "deflate" => new ZLibStream(encoded, CompressionMode.Compress, true),
            "deflate-raw" => new DeflateStream(encoded, CompressionMode.Compress, true),
            _ => new BrotliStream(encoded, CompressionMode.Compress, true) })
            compressor.Write(bytes);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serving = ServeAsync(listener, encoded.ToArray(), encoding == "deflate-raw" ? "deflate" : encoding, cancellation.Token);
        var dataFolder = Path.Combine(Path.GetTempPath(), "ApiConnectToolDecompression-" + Guid.NewGuid());
        using var provider = (ServiceProvider)Program.BuildServiceProvider(dataFolder);
        var service = provider.GetRequiredService<IRequestExecutionService>();
        var request = new RequestItem { Url = route == "proxy" ? "http://apiconnecttool-compression.invalid/body" : address + "/body",
            IgnoreCertificateErrors = route == "tls", ProxyUrl = route == "proxy" ? address : string.Empty };
        if (overrideHeaders) request.Headers = new() { new() { Key = "user-agent", Value = "Custom/2" },
            new() { Key = "ACCEPT", Value = "application/custom" }, new() { Key = "Accept-Encoding", Value = "identity" } };
        var tab = RequestTabViewModel.FromRequest(request, service);
        await tab.SendCommand.ExecuteAsync(null).WaitAsync(cancellation.Token);
        var headers = await serving;
        Assert.True(tab.LastSendSucceeded, tab.ResponseBody);
        Assert.Equal(text, tab.ResponseBody);
        Assert.Equal(bytes, tab.ResponseBytes);
        Assert.Equal("2.00 KB", tab.ResponseSize);
        if (overrideHeaders)
        {
            Assert.Equal("Custom/2", headers["User-Agent"]);
            Assert.Equal("application/custom", headers["Accept"]);
            Assert.Equal("identity", headers["Accept-Encoding"]);
        }
        else
        {
            Assert.StartsWith("ApiConnectTool/", headers["User-Agent"]);
            Assert.Equal("*/*", headers["Accept"]);
            Assert.Equal("gzip, deflate, br", headers["Accept-Encoding"]);
        }
        Assert.DoesNotContain(tab.ResponseHeaders, header => header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Dictionary<string, string>> ServeAsync(TcpListener listener, byte[] bytes, string encoding, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await reader.ReadLineAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':');
            headers.Add(line[..colon], line[(colon + 1)..].Trim());
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Encoding: {encoding}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        return headers;
    }
}
