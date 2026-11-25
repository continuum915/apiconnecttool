using System.Net;
using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class DownloadReplayTransportTests
{
    [Fact]
    public async Task ReplayStreamsBeyondPreviewAndCannotBeChangedThroughReturnedSnapshots()
    {
        var variables = new Dictionary<string, string> { ["value"] = "original" };
        var secretNames = new HashSet<string> { "value" };
        var urls = new List<string>();
        var service = new ApiConnectTool.Services.RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            urls.Add(message.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("complete payload") });
        })));
        var request = new RequestItem { Url = "https://example.test/{{value}}" };
        var run = await new ApiConnectTool.Services.RequestRunner(service).RunAsync(request,
            new() { Variables = variables, SecretVariableNames = secretNames, MaxResponseBytes = 3 });
        Assert.True(run.Execution.BodyWasTruncated);
        Assert.Equal("com"u8.ToArray(), run.Execution.Body);
        var replay = Assert.IsType<RequestReplay>(run.Replay);
        variables["value"] = "changed";
        secretNames.Clear();
        request.Url = "https://edited.test";
        replay.CreateRequest().Url = "https://modified-snapshot.test";
        Assert.IsType<Dictionary<string, string>>(replay.CreateOptions().Variables)["value"] = "modified-snapshot";
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            var result = await service.DownloadAsync(replay.CreateRequest(), destination, replay.CreateOptions());
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "https://example.test/original", "https://example.test/original" }, urls);
            Assert.Equal("complete payload", await File.ReadAllTextAsync(destination));
            Assert.Contains("original", result.ResolvedSecretValues);
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrFrozenTimeoutPreservesDestinationAndRemovesPartialFile(bool timeout)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new ApiConnectTool.Services.RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ++calls == 1 ? new StringContent("complete") : new StreamContent(new BlockingStream(blocked))
            }))));
        var run = await new ApiConnectTool.Services.RequestRunner(service).RunAsync(new() { Url = "https://example.test" },
            new() { Timeout = timeout ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(10), MaxResponseBytes = 2 });
        var directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "response.bin");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await File.WriteAllTextAsync(destination, "previous complete file");
            var download = service.DownloadAsync(run.Replay!.CreateRequest(), destination, run.Replay.CreateOptions(),
                cancellationToken: cancellation.Token);
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("previous complete file", await File.ReadAllTextAsync(destination));
            if (!timeout) cancellation.Cancel();
            var result = await download.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(timeout ? RequestExecutionFailureKind.Timeout : RequestExecutionFailureKind.Canceled, result.FailureKind);
            Assert.Equal("previous complete file", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(RequestBodyMode.Binary)]
    [InlineData(RequestBodyMode.FormData)]
    public async Task ReplayRetainsFileReferencesAndReadsCurrentFileBytes(RequestBodyMode bodyMode)
    {
        var bodies = new List<string>();
        var service = new ApiConnectTool.Services.RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            bodies.Add(await message.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK);
        })));
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        var destination = source + ".download";
        try
        {
            await File.WriteAllTextAsync(source, "original file content");
            var run = await new ApiConnectTool.Services.RequestRunner(service).RunAsync(new()
            {
                Method = HttpMethod.Post, Url = "https://example.test", BodyMode = bodyMode, BodyFilePath = source,
                FormData = new() { new() { Key = "upload", FieldType = FormDataFieldType.File, FilePath = source } }
            });
            Assert.True(run.Execution.IsSuccess, run.Execution.ErrorMessage);
            await File.WriteAllTextAsync(source, "changed file content");
            Assert.True((await service.DownloadAsync(run.Replay!.CreateRequest(), destination, run.Replay.CreateOptions())).IsSuccess);
            Assert.Contains("original file content", bodies[0]);
            Assert.Contains("changed file content", bodies[1]);
            File.Delete(source);
            var missing = await service.DownloadAsync(run.Replay.CreateRequest(), destination, run.Replay.CreateOptions());
            Assert.False(missing.IsSuccess);
            Assert.Equal(2, bodies.Count);
        }
        finally { File.Delete(source); File.Delete(destination); }
    }

    private sealed class BlockingStream(TaskCompletionSource blocked) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        private bool _read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_read) { _read = true; return await base.ReadAsync(buffer, cancellationToken); }
            blocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
