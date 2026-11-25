using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class RequestTabViewModelTests
{
    [Fact]
    public void NewTab_StartsEmptyWithEditableRows()
    {
        var tab = new RequestTabViewModel();

        Assert.Equal("New Request", tab.Name);
        Assert.Empty(tab.RequestUrl);
        Assert.Single(tab.QueryParams);
        Assert.Single(tab.RequestHeaders);
        Assert.Single(tab.FormData);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void CreateSnapshot_PreservesCompleteRequestDefinition()
    {
        var id = Guid.NewGuid();
        var tab = new RequestTabViewModel
        {
            Name = "Create pet",
            RequestUrl = "https://api.example.test/pets",
            SelectedMethodIndex = 1,
            BodyMode = RequestBodyMode.FormData,
            BodyContentType = "multipart/form-data",
            RequestBody = "ignored raw body",
            BodyFilePath = "C:\\fixtures\\pet.png",
            Auth = new RequestAuth { Type = AuthType.ApiKey, Token = "secret", ApiKeyHeaderName = "X-API-Key" }
        };
        tab.QueryParams[0].Key = "include";
        tab.QueryParams[0].Value = "owner";
        tab.RequestHeaders[0].Key = "Accept";
        tab.RequestHeaders[0].Value = "application/json";
        tab.FormData[0].Key = "name";
        tab.FormData[0].Value = "Mochi";

        var snapshot = tab.CreateSnapshot(id);

        Assert.Equal(id, snapshot.Id);
        Assert.Equal("POST", snapshot.Method.Method);
        Assert.Equal("https://api.example.test/pets", snapshot.Url);
        Assert.Equal(RequestBodyMode.FormData, snapshot.BodyMode);
        Assert.Equal("multipart/form-data", snapshot.BodyContentType);
        Assert.Equal("C:\\fixtures\\pet.png", snapshot.BodyFilePath);
        Assert.Single(snapshot.QueryParams);
        Assert.Single(snapshot.Headers);
        Assert.Single(snapshot.FormData);
        Assert.Equal(AuthType.ApiKey, snapshot.Auth.Type);
        Assert.Equal("secret", snapshot.Auth.Token.Value);
    }

    [Fact]
    public void FromRequest_LoadsCompletePersistedStateWithoutDirtyingTab()
    {
        var request = new RequestItem
        {
            Id = Guid.NewGuid(),
            Name = "Search",
            Url = "https://api.example.test/search",
            Body = "{\"q\":\"apiconnecttool\"}",
            BodyContentType = "application/json",
            QueryParams = new() { new KeyValueItem { Key = "page", Value = "2" } },
            Headers = new() { new KeyValueItem { Key = "Accept", Value = "application/json" } },
            FormData = new() { new KeyValueItem { Key = "unused", Value = "value" } },
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "token" }
        };

        var tab = RequestTabViewModel.FromRequest(request);

        Assert.Equal(request.Id, tab.RequestId);
        Assert.Equal("Search", tab.Name);
        Assert.Equal("page", tab.QueryParams[0].Key);
        Assert.Equal("Accept", tab.RequestHeaders[0].Key);
        Assert.Equal(AuthType.Bearer, tab.Auth.Type);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void EditingRequestState_MarksTabDirty()
    {
        var tab = new RequestTabViewModel();
        tab.RequestHeaders[0].Key = "Accept";

        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void MarkSaved_AssignsIdentityAndClearsDirtyState()
    {
        var tab = new RequestTabViewModel { RequestUrl = "https://example.test" };
        var id = Guid.NewGuid();

        tab.MarkSaved(id);

        Assert.Equal(id, tab.RequestId);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task DownloadResponseAsync_WithoutPriorSend_FailsWithoutCallingService()
    {
        var service = new FakeExecutionService();
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test" };

        var succeeded = await tab.DownloadResponseAsync("C:\\downloads\\file.bin");

        Assert.False(succeeded);
        Assert.False(service.DownloadWasCalled);
        Assert.Contains("Send a request", tab.ResponseActionMessage);
    }

    [Fact]
    public async Task DownloadResponseAsync_AfterSuccessfulSend_StreamsUsingLastSentRequest()
    {
        var service = new FakeExecutionService();
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test/pets" };

        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.CanDownloadResponse);

        var succeeded = await tab.DownloadResponseAsync("C:\\downloads\\file.bin");

        Assert.True(succeeded);
        Assert.True(service.DownloadWasCalled);
        Assert.Equal("https://example.test/pets", service.LastDownloadRequest!.Url);
        Assert.Equal("C:\\downloads\\file.bin", service.LastDownloadDestination);
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentVariables_PassesResolvedVariablesToExecutionService()
    {
        var service = new FakeExecutionService();
        var environment = new RequestEnvironment
        {
            Variables = { new EnvironmentVariable { Key = "host", InitialValue = new EncryptedString("example.test") } }
        };
        var tab = new RequestTabViewModel(service, () => environment)
        {
            RequestUrl = "https://{{host}}/pets"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastExecutionOptions?.Variables);
        Assert.Equal("example.test", service.LastExecutionOptions!.Variables!["host"]);
        Assert.Null(tab.ResponseActionMessage);
    }

    [Fact]
    public async Task SendAsync_WithUnresolvedVariable_SetsWarningButStillSends()
    {
        var service = new FakeExecutionService();
        var tab = new RequestTabViewModel(service, () => null)
        {
            RequestUrl = "https://example.test/{{missing}}"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.True(service.ExecuteWasCalled);
        Assert.Contains("missing", tab.ResponseActionMessage);
    }

    [Fact]
    public async Task SendAsync_ProductionEnvironmentDeclined_BlocksSend()
    {
        var service = new FakeExecutionService();
        var environment = new RequestEnvironment { Name = "Prod", IsProduction = true };
        var tab = new RequestTabViewModel(service, () => environment, _ => Task.FromResult(false))
        {
            RequestUrl = "https://example.test"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.False(service.ExecuteWasCalled);
        Assert.Contains("production", tab.ResponseActionMessage);
    }

    [Fact]
    public async Task SendAsync_ProductionEnvironmentConfirmed_AllowsSend()
    {
        var service = new FakeExecutionService();
        var environment = new RequestEnvironment { Name = "Prod", IsProduction = true };
        var tab = new RequestTabViewModel(service, () => environment, _ => Task.FromResult(true))
        {
            RequestUrl = "https://example.test"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.True(service.ExecuteWasCalled);
    }

    [Fact]
    public async Task SendAsync_NonProductionEnvironment_DoesNotPromptForConfirmation()
    {
        var service = new FakeExecutionService();
        var environment = new RequestEnvironment { Name = "Dev", IsProduction = false };
        var promptCalled = false;
        var tab = new RequestTabViewModel(service, () => environment, _ => { promptCalled = true; return Task.FromResult(true); })
        {
            RequestUrl = "https://example.test"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.True(service.ExecuteWasCalled);
        Assert.False(promptCalled);
    }

    [Fact]
    public async Task SendAsync_SuccessfulSend_FiresOnHistoryRecordedExactlyOnceWithSuccessShape()
    {
        var service = new FakeExecutionService();
        var recorded = new List<HistoryEntry>();
        var tab = new RequestTabViewModel(service, () => null, null, null, recorded.Add)
        {
            Name = "List pets",
            RequestUrl = "https://example.test/pets"
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.True(entry.IsSuccess);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal("https://example.test/pets", entry.RequestSnapshot.Url);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_FailedSend_FiresOnHistoryRecordedExactlyOnceWithFailureShape()
    {
        var service = new FakeExecutionService
        {
            NextResultFactory = () => RequestExecutionResult.Failed(RequestExecutionFailureKind.Dns, "Could not resolve host.")
        };
        var recorded = new List<HistoryEntry>();
        var tab = new RequestTabViewModel(service, () => null, null, null, recorded.Add)
        {
            RequestUrl = "https://example.test/pets"
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.False(entry.IsSuccess);
        Assert.Null(entry.StatusCode);
        Assert.Equal("Could not resolve host.", entry.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_RequestSnapshotInHistory_KeepsUnresolvedVariableTokensInsteadOfLiteralValues()
    {
        var service = new FakeExecutionService();
        var environment = new RequestEnvironment
        {
            Variables = { new EnvironmentVariable { Key = "host", InitialValue = new EncryptedString("example.test") } }
        };
        var recorded = new List<HistoryEntry>();
        var tab = new RequestTabViewModel(service, () => environment, null, null, recorded.Add)
        {
            RequestUrl = "https://{{host}}/pets"
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.Equal("https://{{host}}/pets", entry.RequestSnapshot.Url);
    }

    [Fact]
    public async Task SendAsync_ResponseWithSecret_NeverContainsTheLiteralSecretInRecordedHistory()
    {
        // The Bearer token comes from a secret-flagged environment variable (the realistic case
        // this redaction exists for) rather than a value typed directly into the Auth field, so
        // RequestSnapshot itself only ever carries the {{apiToken}} template, never the literal -
        // consistent with 4a/4c's design: never store the raw value and redact later.
        var service = new FakeExecutionService
        {
            NextResultFactory = () => new RequestExecutionResult
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                ReasonPhrase = "OK",
                ContentType = "text/plain",
                Body = "token echoed: top-secret-token"u8.ToArray(),
                Headers = [new KeyValueItem { Key = "X-Echo-Auth", Value = "Bearer top-secret-token" }],
                ResolvedSecretValues = ["top-secret-token"]
            }
        };
        var environment = new RequestEnvironment
        {
            Variables = { new EnvironmentVariable { Key = "apiToken", InitialValue = new EncryptedString("top-secret-token"), IsSecret = true } }
        };
        var recorded = new List<HistoryEntry>();
        var tab = new RequestTabViewModel(service, () => environment, null, null, recorded.Add)
        {
            RequestUrl = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "{{apiToken}}" }
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.Equal("{{apiToken}}", entry.RequestSnapshot.Auth.Token.Value);
        var json = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("top-secret-token", json);
        Assert.Contains("••••••••", entry.RedactedResponseBodyPreview);
        Assert.Contains("••••••••", entry.RedactedResponseHeadersText);
    }

    [Fact]
    public async Task SendAsync_RawBodyWithinTheCap_IsStoredWholeInHistory()
    {
        var recorded = new List<HistoryEntry>();
        var body = new string('a', HistoryRequestBody.MaxBytes);
        var tab = new RequestTabViewModel(new FakeExecutionService(), () => null, null, null, recorded.Add)
        {
            RequestUrl = "https://example.test/upload",
            RequestBody = body
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.Equal(body, entry.RequestSnapshot.Body);
        Assert.False(entry.RequestBodyWasTruncatedForHistory);
    }

    [Fact]
    public async Task SendAsync_LargeRawBody_IsCappedInHistoryButSentWhole()
    {
        var service = new FakeExecutionService();
        var recorded = new List<HistoryEntry>();
        var body = new string('a', HistoryRequestBody.MaxBytes * 4);
        var tab = new RequestTabViewModel(service, () => null, null, null, recorded.Add)
        {
            RequestUrl = "https://example.test/upload",
            RequestBody = body
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.Equal(HistoryRequestBody.MaxBytes, Encoding.UTF8.GetByteCount(entry.RequestSnapshot.Body));
        Assert.True(entry.RequestBodyWasTruncatedForHistory);
        Assert.Equal(body, service.LastRequest!.Body);
        Assert.Equal(body, tab.RequestBody);
    }

    [Fact]
    public async Task SendAsync_LargeBody_LeavesFormDataRowsAndFileReferencesUntouched()
    {
        var recorded = new List<HistoryEntry>();
        var longValue = new string('v', HistoryRequestBody.MaxBytes * 2);
        var tab = RequestTabViewModel.FromRequest(new RequestItem
        {
            Url = "https://example.test/upload",
            BodyMode = RequestBodyMode.FormData,
            FormData =
            {
                new KeyValueItem { Key = "notes", Value = longValue },
                new KeyValueItem { Key = "file", Value = @"C:\uploads\photo.png", FieldType = FormDataFieldType.File }
            }
        }, new FakeExecutionService(), () => null, null, null, recorded.Add);

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.Equal(longValue, entry.RequestSnapshot.FormData.Single(row => row.Key == "notes").Value);
        Assert.Equal(@"C:\uploads\photo.png", entry.RequestSnapshot.FormData.Single(row => row.Key == "file").Value);
        Assert.False(entry.RequestBodyWasTruncatedForHistory);
    }

    [Fact]
    public async Task SendAsync_SecretAtTheCapBoundary_IsRedactedBeforeTheBodyIsShortened()
    {
        var service = new FakeExecutionService
        {
            NextResultFactory = () => new RequestExecutionResult
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                ContentType = "text/plain",
                Body = "ok"u8.ToArray(),
                ResolvedSecretValues = ["top-secret-token"]
            }
        };
        var recorded = new List<HistoryEntry>();
        var tab = new RequestTabViewModel(service, () => null, null, null, recorded.Add)
        {
            RequestUrl = "https://example.test/upload",
            RequestBody = "early top-secret-token " + new string('a', HistoryRequestBody.MaxBytes - 30) + "top-secret-token" + new string('b', 100)
        };

        await tab.SendCommand.ExecuteAsync(null);

        var entry = Assert.Single(recorded);
        Assert.True(entry.RequestBodyWasTruncatedForHistory);
        Assert.DoesNotContain("top-secret", entry.RequestSnapshot.Body);
        Assert.DoesNotContain("top-se", entry.RequestSnapshot.Body);
        Assert.StartsWith("early ••••••••", entry.RequestSnapshot.Body);
    }

    [Fact]
    public async Task SendAsync_WithCookieJar_PopulatesResponseCookiesForTheFinalUriAfterRedirects()
    {
        var finalUri = new Uri("https://example.test/final");
        var jar = new CookieContainer();
        jar.Add(finalUri, new Cookie("session", "abc123") { Expires = DateTime.UtcNow.AddDays(1) });
        // A cookie scoped to the original (pre-redirect) host must not leak into a different
        // final origin's cookie list.
        jar.Add(new Uri("https://elsewhere.test"), new Cookie("other", "nope") { Expires = DateTime.UtcNow.AddDays(1) });
        var cookieJarService = new FakeCookieJarService(jar);
        var service = new FakeExecutionService
        {
            NextResultFactory = () => new RequestExecutionResult
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                ContentType = "text/plain",
                Body = "ok"u8.ToArray(),
                FinalUri = finalUri
            }
        };
        var tab = new RequestTabViewModel(service, () => null, null, null, null, cookieJarService)
        {
            RequestUrl = "https://example.test/start"
        };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.True(tab.HasCookies);
        var cookie = Assert.Single(tab.ResponseCookies);
        Assert.Equal("session", cookie.Name);
        Assert.Equal("abc123", cookie.Value);
        Assert.True(cookieJarService.SaveWasCalled);
    }

    [Fact]
    public async Task SendAsync_FailedSend_ClearsAnyPreviousResponseCookies()
    {
        var finalUri = new Uri("https://example.test/final");
        var jar = new CookieContainer();
        jar.Add(finalUri, new Cookie("session", "abc123") { Expires = DateTime.UtcNow.AddDays(1) });
        var cookieJarService = new FakeCookieJarService(jar);
        var service = new FakeExecutionService
        {
            NextResultFactory = () => new RequestExecutionResult
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                ContentType = "text/plain",
                Body = "ok"u8.ToArray(),
                FinalUri = finalUri
            }
        };
        var tab = new RequestTabViewModel(service, () => null, null, null, null, cookieJarService)
        {
            RequestUrl = "https://example.test/start"
        };
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.HasCookies);

        service.NextResultFactory = () => RequestExecutionResult.Failed(RequestExecutionFailureKind.Dns, "boom");
        await tab.SendCommand.ExecuteAsync(null);

        Assert.False(tab.HasCookies);
        Assert.Empty(tab.ResponseCookies);
    }

    [Fact]
    public void ClearCookiesCommand_ClearsJarAndTabsOwnCookieList()
    {
        var finalUri = new Uri("https://example.test/final");
        var jar = new CookieContainer();
        jar.Add(finalUri, new Cookie("session", "abc123") { Expires = DateTime.UtcNow.AddDays(1) });
        var cookieJarService = new FakeCookieJarService(jar);
        var tab = new RequestTabViewModel(new FakeExecutionService(), () => null, null, null, null, cookieJarService);

        tab.ClearCookiesCommand.Execute(null);

        Assert.True(cookieJarService.ClearWasCalled);
    }

    [Fact]
    public void BuildCurlCommand_ResolvesAgainstCurrentlySelectedEnvironment()
    {
        var environment = new RequestEnvironment
        {
            Variables = { new EnvironmentVariable { Key = "host", InitialValue = new EncryptedString("example.test") } }
        };
        var tab = new RequestTabViewModel(new FakeExecutionService(), () => environment)
        {
            RequestUrl = "https://{{host}}/pets"
        };

        var command = tab.BuildCurlCommand(revealSecrets: true);

        Assert.Contains("https://example.test/pets", command);
    }

    [Fact]
    public void BuildCurlCommand_DefaultRedacted_NeverContainsTheAuthToken()
    {
        var tab = new RequestTabViewModel(new FakeExecutionService(), () => null)
        {
            RequestUrl = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };

        var command = tab.BuildCurlCommand(revealSecrets: false);

        Assert.DoesNotContain("top-secret-token", command);
    }

    [Fact]
    public void BuildCurlCommand_RevealSecretsTrue_ContainsTheLiteralAuthToken()
    {
        var tab = new RequestTabViewModel(new FakeExecutionService(), () => null)
        {
            RequestUrl = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };

        var command = tab.BuildCurlCommand(revealSecrets: true);

        Assert.Contains("top-secret-token", command);
    }

    [Fact]
    public void BuildCurlCommand_NoServiceConfigured_StillProducesACommand()
    {
        var tab = new RequestTabViewModel { RequestUrl = "https://example.test" };

        var command = tab.BuildCurlCommand(revealSecrets: true);

        Assert.Contains("https://example.test", command);
    }

    private sealed class FakeCookieJarService(CookieContainer container) : ICookieJarService
    {
        public CookieContainer Container { get; } = container;
        public bool SaveWasCalled { get; private set; }
        public bool ClearWasCalled { get; private set; }
        public void Save() => SaveWasCalled = true;
        public void Clear() => ClearWasCalled = true;
    }

    [Fact]
    public async Task SendAsync_WithoutHistoryHook_DoesNotThrow()
    {
        var service = new FakeExecutionService();
        var tab = new RequestTabViewModel(service) { RequestUrl = "https://example.test" };

        await tab.SendCommand.ExecuteAsync(null);

        Assert.True(service.ExecuteWasCalled);
    }

    [Fact]
    public async Task Send_EvaluatesSnapshotAssertionsAndCarriesThemIntoHistory()
    {
        HistoryEntry? history = null;
        var request = new RequestItem { Assertions = new() { new(), new() { ExpectedValue = "201" } } };
        var execution = new FakeExecutionService();
        var tab = RequestTabViewModel.FromRequest(request, execution, onHistoryRecorded: entry => history = entry);
        // Edits during the send belong to the next send, not this response.
        execution.NextResultFactory = () =>
        {
            tab.AssertionTests.Assertions[0].ExpectedValue = "500";
            return new() { IsSuccess = true, StatusCode = HttpStatusCode.OK };
        };
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.AssertionTests.Results[0].Passed);
        Assert.False(tab.AssertionTests.Results[1].Passed);
        Assert.Equal("200", history!.RequestSnapshot.Assertions[0].ExpectedValue);
        Assert.Equal(request.Assertions[0].Id, history.RequestSnapshot.Assertions[0].Id);
    }

    [Fact]
    public async Task Send_WithNoAssertions_ReturnsEmptyResults()
    {
        var tab = new RequestTabViewModel(new FakeExecutionService());
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Empty(tab.AssertionTests.Results);
        Assert.Empty(tab.CreateSnapshot().Assertions);
    }

    [Theory]
    [InlineData(RequestExecutionFailureKind.Timeout)]
    [InlineData(RequestExecutionFailureKind.Network)]
    [InlineData(RequestExecutionFailureKind.Canceled)]
    [InlineData(RequestExecutionFailureKind.Validation)]
    public async Task FailedSend_DoesNotEvaluateTests(RequestExecutionFailureKind failure)
    {
        var execution = new FakeExecutionService();
        var tab = new RequestTabViewModel(execution);
        tab.AssertionTests.Assertions.Add(new());
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Single(tab.AssertionTests.Results);
        execution.NextResultFactory = () => RequestExecutionResult.Failed(failure, "failed");
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Empty(tab.AssertionTests.Results);
        Assert.Contains(tab.AssertionTests.Summary, new[] { "Str.Assertions.NotEvaluated", "Tests were not evaluated" });
    }

    [Fact]
    public async Task InvalidTimeout_ClearsStaleTestsBeforeSending()
    {
        var tab = new RequestTabViewModel(new FakeExecutionService());
        tab.AssertionTests.Assertions.Add(new());
        await tab.SendCommand.ExecuteAsync(null);
        tab.TimeoutSeconds = 0;
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Empty(tab.AssertionTests.Results);
        Assert.Contains(tab.AssertionTests.Summary, new[] { "Str.Assertions.NotEvaluated", "Tests were not evaluated" });
    }

    private sealed class FakeExecutionService : IRequestExecutionService
    {
        public bool DownloadWasCalled { get; private set; }
        public bool ExecuteWasCalled { get; private set; }
        public RequestItem? LastDownloadRequest { get; private set; }
        public string? LastDownloadDestination { get; private set; }
        public RequestExecutionOptions? LastExecutionOptions { get; private set; }
        public RequestItem? LastRequest { get; private set; }
        public Func<RequestExecutionResult>? NextResultFactory { get; set; }

        public Task<RequestExecutionResult> ExecuteAsync(RequestItem request, RequestExecutionOptions? options = null, CancellationToken cancellationToken = default)
        {
            ExecuteWasCalled = true;
            LastRequest = request;
            LastExecutionOptions = options;
            return Task.FromResult(NextResultFactory?.Invoke() ?? new RequestExecutionResult
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                ReasonPhrase = "OK",
                ContentType = "text/plain",
                Body = "ok"u8.ToArray()
            });
        }

        public Task<RequestDownloadResult> DownloadAsync(RequestItem request, string destinationPath, RequestExecutionOptions? options = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadWasCalled = true;
            LastDownloadRequest = request;
            LastDownloadDestination = destinationPath;
            progress?.Report(2);
            return Task.FromResult(new RequestDownloadResult { IsSuccess = true, BytesWritten = 2, StatusCode = HttpStatusCode.OK });
        }
    }
}
