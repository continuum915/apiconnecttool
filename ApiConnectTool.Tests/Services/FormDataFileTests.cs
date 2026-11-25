using System.Net;
using System.Text;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class FormDataFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApiConnectToolFormTests-" + Guid.NewGuid());
    private RequestItem FileRequest(string path) => new()
    {
        Method = HttpMethod.Post, Url = "https://example.test", BodyMode = RequestBodyMode.FormData,
        FormData = new() { new() { Key = "description", Value = "hello" },
            new() { Key = "upload", FieldType = FormDataFieldType.File, FilePath = path } }
    };

    [Theory]
    [InlineData("image.png", "image/png", false)]
    [InlineData("data.unknown", "application/octet-stream", true)]
    public async Task MixedMultipartStreamsFilesForIndividualSendsAndCollectionRuns(string name, string mime, bool collectionRun)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        byte[] bytes = [0, 1, 2, 255, 128];
        File.WriteAllBytes(path, bytes);
        var sends = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            sends++;
            var multipart = Assert.IsType<MultipartFormDataContent>(message.Content);
            var parts = multipart.ToArray();
            Assert.Equal(2, parts.Length);
            Assert.Equal("hello", await parts[0].ReadAsStringAsync());
            Assert.IsType<StreamContent>(parts[1]);
            Assert.Equal(bytes, await parts[1].ReadAsByteArrayAsync());
            Assert.Equal(mime, parts[1].Headers.ContentType!.MediaType);
            Assert.Equal(name, parts[1].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("upload", parts[1].Headers.ContentDisposition!.Name!.Trim('"'));
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var request = FileRequest("{{path}}");
        if (collectionRun)
        {
            var collection = new RequestCollection { Requests = new() { request }, Variables = new() { new() { Key = "path", InitialValue = new(path) } } };
            var result = await new CollectionRunner(new RequestRunner(service)).RunAsync(collection);
            Assert.Equal(1, result.PassedCount);
        }
        else
        {
            var result = await service.ExecuteAsync(request, new() { Variables = new Dictionary<string, string> { ["path"] = path } });
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        Assert.Equal(1, sends);
        using var writable = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrLockedFileNamesTheFieldAndSendsNothing(bool locked)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "data.txt");
        FileStream? handle = locked ? new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None) : null;
        using (handle)
        {
            var sends = 0;
            var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })));
            var tab = RequestTabViewModel.FromRequest(FileRequest(path), service);
            await tab.SendCommand.ExecuteAsync(null);
            Assert.Equal(ResponseStatusClass.TransportError, tab.ResponseStatusClass);
            Assert.Contains("upload", tab.DisplayedResponseBody);
            Assert.Equal(0, sends);
        }
    }

    [Fact]
    public async Task UrlEncodedIncludesOnlyTextRows()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            Assert.Equal("description=hello", await message.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var request = FileRequest("missing");
        request.BodyMode = RequestBodyMode.UrlEncoded;
        Assert.True((await service.ExecuteAsync(request)).IsSuccess);
        Assert.DoesNotContain("upload", new CurlGenerator().Generate(request, new Dictionary<string, string>(), false));
    }

    [Fact]
    public async Task FilePathsSurviveSnapshotsWorkspaceHistoryAndPostmanWithoutEmbeddingContents()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "fixture.txt");
        File.WriteAllText(path, "file-content-must-not-be-stored");
        var request = FileRequest(path);
        var tab = RequestTabViewModel.FromRequest(request);
        Assert.Equal(path, tab.CreateSnapshot().FormData[1].FilePath);
        tab.FormData[1].FilePath = path + ".new";
        Assert.True(tab.IsDirty);
        Assert.Equal(FormDataFieldType.File, tab.CreateSnapshot().Clone().FormData[1].FieldType);
        var workspace = new Workspace { Collections = new() { new() { Requests = new() { request } } } };
        var store = new WorkspaceService(_directory);
        store.SaveWorkspace(workspace);
        Assert.Equal(path, store.LoadWorkspace().Collections[0].Requests[0].FormData[1].FilePath);
        var history = new HistoryStore(new WindowsDpapiSecretStore(), _directory);
        history.Add(new() { RequestSnapshot = request });
        await history.FlushAsync();
        Assert.Equal(path, history.GetAll()[0].RequestSnapshot.FormData[1].FilePath);
        Assert.DoesNotContain("file-content-must-not-be-stored", File.ReadAllText(Path.Combine(_directory, "history.json")));
        Assert.DoesNotContain("file-content-must-not-be-stored", File.ReadAllText(Path.Combine(_directory, "workspace.json")));
        var converter = new PostmanCollectionConverter();
        var json = converter.Export(workspace.Collections[0], false);
        using var document = JsonDocument.Parse(json);
        var file = document.RootElement.GetProperty("item")[0].GetProperty("request").GetProperty("body").GetProperty("formdata")[1];
        Assert.Equal("file", file.GetProperty("type").GetString());
        Assert.Equal(path, file.GetProperty("src").GetString());
        var imported = converter.Import(json).Requests[0].FormData[1];
        Assert.Equal(FormDataFieldType.File, imported.FieldType);
        Assert.Equal(path, imported.FilePath);
        Assert.Contains("-F 'upload=@" + path + "'", new CurlGenerator().Generate(request, new Dictionary<string, string>(), false));
        var exports = new CollectionExportService();
        Assert.Equal(path, exports.Import(exports.Export(workspace.Collections[0], workspace.Id, false)).Requests[0].FormData[1].FilePath);
    }

    [Fact]
    public void SchemaEightJsonMigratesExistingFormFieldsToText()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "workspace.json"),
            """{"SchemaVersion":8,"Collections":[{"Requests":[{"FormData":[{"Key":"field","Value":"old text"}]}]}]}""");
        var store = new WorkspaceService(_directory);
        var workspace = store.LoadWorkspace();
        Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
        var row = workspace.Collections[0].Requests[0].FormData[0];
        Assert.Equal(FormDataFieldType.Text, row.FieldType);
        Assert.Equal("old text", row.Value);
        Assert.Equal("", row.FilePath);
        store.SaveWorkspace(workspace);
        Assert.Equal(FormDataFieldType.Text, store.LoadWorkspace().Collections[0].Requests[0].FormData[0].FieldType);
    }

    [Fact]
    public void PostmanReportDoesNotClaimSupportedFileFieldsWereImportedAsText()
    {
        var result = new PostmanCollectionConverter().ImportWithReport(
            """{"info":{"name":"Files"},"item":[{"name":"Upload","request":{"method":"POST","url":{"raw":"https://example.test"},"body":{"mode":"formdata","formdata":[{"key":"upload","type":"file","src":"C:/image.png"}]}}}]}""");
        Assert.Equal(FormDataFieldType.File, result.Collection.Requests[0].FormData[0].FieldType);
        Assert.Empty(result.Report.Warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
