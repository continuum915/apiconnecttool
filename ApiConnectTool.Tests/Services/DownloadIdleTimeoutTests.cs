using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>Downloads use the request timeout for the response headers and then as an idle
/// limit between chunks; Send keeps its total timeout.</summary>
public class DownloadIdleTimeoutTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Returns <paramref name="chunks"/> chunks of <paramref name="chunkSize"/> bytes,
    /// waiting <paramref name="delay"/> before each one; after them it stalls forever when
    /// <paramref name="stallAtEnd"/> is set, otherwise it ends.</summary>
    private sealed class SlowStream(int chunks, int chunkSize, TimeSpan delay, bool stallAtEnd = false) : Stream
    {
        private int _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent >= chunks)
            {
                if (stallAtEnd) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            await Task.Delay(delay, cancellationToken);
            var count = Math.Min(chunkSize, buffer.Length);
            buffer.Span[..count].Fill((byte)'x');
            _sent++;
            return count;
        }
    }

    private static RequestExecutionService Service(Func<HttpResponseMessage> respond, TimeSpan? headerDelay = null) =>
        new(new TestHttpClientFactory(new FakeHttpMessageHandler(async _ =>
        {
            if (headerDelay is { } wait) await Task.Delay(wait);
            return respond();
        })));

    private static async Task WithDirectory(Func<string, string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "download.bin");
        try { await test(directory, destination); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ASteadilyFlowingDownloadLongerThanTheTimeoutSucceeds()
    {
        // 12 chunks, 100 ms apart: about 1.2 s in total against a 400 ms timeout.
        var service = Service(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(12, 10, TimeSpan.FromMilliseconds(100))) });
        await WithDirectory(async (_, destination) =>
        {
            var result = await service.DownloadAsync(new RequestItem { Url = "https://example.test/big" }, destination, new() { Timeout = RequestTimeout });
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.Elapsed > RequestTimeout);
            Assert.Equal(120, result.BytesWritten);
            Assert.Equal(120, new FileInfo(destination).Length);
        });
    }

    [Fact]
    public async Task AStalledStreamFailsWithTheStallMessageAndLeavesTheDestinationUntouched()
    {
        var service = Service(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(2, 10, TimeSpan.FromMilliseconds(50), stallAtEnd: true)) });
        await WithDirectory(async (directory, destination) =>
        {
            await File.WriteAllTextAsync(destination, "previous complete file");
            var result = await service.DownloadAsync(new RequestItem { Url = "https://example.test/big" }, destination,
                new() { Timeout = TimeSpan.FromSeconds(1) }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(result.IsSuccess);
            Assert.Equal(RequestExecutionFailureKind.Timeout, result.FailureKind);
            Assert.Equal("Download stalled: no data received for 1 seconds.", result.ErrorMessage);
            Assert.Equal("previous complete file", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.GetFiles(directory));
        });
    }

    [Fact]
    public async Task SlowHeadersTimeOutAsARequestTimeout()
    {
        var service = Service(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("late") }, headerDelay: TimeSpan.FromSeconds(30));
        await WithDirectory(async (directory, destination) =>
        {
            var result = await service.DownloadAsync(new RequestItem { Url = "https://example.test/slow" }, destination,
                new() { Timeout = RequestTimeout }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(RequestExecutionFailureKind.Timeout, result.FailureKind);
            Assert.DoesNotContain("stalled", result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public async Task UserCancellationDuringAFlowingDownloadIsReportedAsCanceled()
    {
        var service = Service(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(1000, 10, TimeSpan.FromMilliseconds(20))) });
        await WithDirectory(async (directory, destination) =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var result = await service.DownloadAsync(new RequestItem { Url = "https://example.test/big" }, destination,
                new() { Timeout = TimeSpan.FromSeconds(5) }, cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(RequestExecutionFailureKind.Canceled, result.FailureKind);
            Assert.Empty(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public async Task SendKeepsItsTotalTimeoutForASteadilyFlowingBody()
    {
        var service = Service(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(12, 10, TimeSpan.FromMilliseconds(100))) });
        var result = await service.ExecuteAsync(new RequestItem { Url = "https://example.test/big" }, new() { Timeout = RequestTimeout })
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RequestExecutionFailureKind.Timeout, result.FailureKind);
    }
}
