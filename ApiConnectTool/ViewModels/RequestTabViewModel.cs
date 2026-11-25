using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public partial class RequestTabViewModel : ObservableObject
{
    /// <summary>The methods the request bar offers, in order.</summary>
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
    private static readonly IVariableResolver Resolver = new VariableResolver();
    private readonly IRequestExecutionService? _requestExecutionService;
    private readonly Func<RequestEnvironment?>? _environmentProvider;
    private readonly Func<IReadOnlyList<ProductionTarget>, Task<bool>>? _confirmProductionSend;
    private readonly Action<HistoryEntry>? _onHistoryRecorded;
    private readonly ICookieJarService? _cookieJarService;
    [ObservableProperty] private string? _cookiePersistenceWarning;
    [ObservableProperty] private string? _historyPersistenceWarning;
    // Set when this tab was reopened from a history entry whose stored body was cut short. Only
    // the raw editor shows that text, so other body modes have nothing incomplete to warn about.
    private bool _requestBodyIsIncomplete;
    public string? RequestBodyIncompleteNotice => _requestBodyIsIncomplete && IsRawBody ? HistoryRequestBody.IncompleteNotice : null;
    public void ShowIncompleteBodyNotice() { _requestBodyIsIncomplete = true; OnPropertyChanged(nameof(RequestBodyIncompleteNotice)); }
    private readonly ICurlGenerator _curlGenerator;
    private static readonly ISecretRedactor HistoryRedactor = new SecretRedactor();
    private const int MaxHistoryBodyPreviewBytes = 16 * 1024;
    private bool _trackingChanges;
    private readonly RequestQueryEditor _queryEditor;
    private RequestAuth? _observedAuth;
    private CancellationTokenSource? _sendCancellationSource;
    private CancellationTokenSource? _downloadCancellationSource;
    private RequestReplay? _lastReplay;
    private IReadOnlyList<ProductionTarget> _lastProductionTargets = [];
    public Func<Workspace?>? WorkspaceProvider { get; set; }
    public Action? SaveScopes { get; set; }
    public Func<RequestCollection?>? CollectionProvider { get; set; }
    /// <summary>The shared history storage warning; shown once this tab has recorded a send.</summary>
    public Func<string?>? HistoryWarningProvider { get; set; }
    private bool _hasRecordedHistory;
    public void RefreshHistoryWarning() => HistoryPersistenceWarning = _hasRecordedHistory ? HistoryWarningProvider?.Invoke() : null;
    public AuthEditorViewModel AuthEditor { get; private set; } = null!;
    public void RefreshAuthContext() { AuthEditor.ObserveParents(CollectionProvider?.Invoke()); Summary.Recount(); Summary.RefreshProduction(); }
    /// <summary>Tab-header counts and the production strip.</summary>
    public RequestEditorSummary Summary { get; }
    /// <summary>Called after every send with the request snapshot and its result (the Console records it).</summary>
    public Action<RequestItem, RequestRunResult>? RunCompleted { get; set; }
    /// <summary>Response-viewer counts, status chip and test outcome.</summary>
    public ResponseSummary Response { get; }

    private VariableScopeMap BuildVariables(RequestEnvironment? environment) => CreateScriptScopes(environment).Build();
    private ScriptVariableScopes CreateScriptScopes(RequestEnvironment? environment, Action? save = null) => new(
        WorkspaceProvider?.Invoke(), CollectionProvider?.Invoke(), environment, save, collectionEnvironment: CollectionProvider?.Invoke()?.ActiveEnvironment);

    public VariableInspectorViewModel InspectVariables(VariableInspectorViewModel? observer = null)
    {
        var workspace = WorkspaceProvider?.Invoke();
        var collection = CollectionProvider?.Invoke();
        var environment = _environmentProvider?.Invoke();
        var inspector = new VariableInspectorViewModel(CreateExecutionSnapshot(), BuildVariables(environment));
        var subscriptionOwner = observer ?? inspector;
        subscriptionOwner.Observe(this);
        subscriptionOwner.Observe(Auth);
        subscriptionOwner.Observe(AssertionTests);
        if (workspace is not null)
        {
            subscriptionOwner.Observe((System.Collections.Specialized.INotifyCollectionChanged)workspace.Collections);
            subscriptionOwner.Observe((System.Collections.Specialized.INotifyCollectionChanged)workspace.Environments);
        }
        foreach (var rows in new[] { QueryParams, RequestHeaders, FormData })
        {
            subscriptionOwner.Observe((System.Collections.Specialized.INotifyCollectionChanged)rows);
            foreach (var row in rows) subscriptionOwner.Observe(row);
        }
        foreach (var rows in new[] { workspace?.GlobalVariables, collection?.Variables, environment?.Variables, collection?.ActiveEnvironment?.Variables })
        {
            if (rows is null) continue;
            subscriptionOwner.Observe((System.Collections.Specialized.INotifyCollectionChanged)rows);
            foreach (var row in rows) subscriptionOwner.Observe(row);
        }
        if (collection is not null)
        {
            subscriptionOwner.Observe(collection);
            subscriptionOwner.Observe(collection.Auth);
            foreach (var list in new System.Collections.Specialized.INotifyCollectionChanged[] { collection.Requests, collection.Folders, collection.Environments }) subscriptionOwner.Observe(list);
            foreach (var folder in collection.Folders) { subscriptionOwner.Observe(folder); subscriptionOwner.Observe(folder.Auth); }
        }
        if (observer is null) inspector.SetRefresh(() => InspectVariables(inspector));
        return inspector;
    }

    [ObservableProperty] private Guid? _requestId;
    [ObservableProperty] private string _method = "GET";
    [ObservableProperty] private string _name = "New Request";
    [ObservableProperty] private string _requestUrl = string.Empty;
    [ObservableProperty] private string _requestBody = string.Empty;
    [ObservableProperty] private RequestBodyMode _bodyMode = RequestBodyMode.Raw;
    [ObservableProperty] private string _bodyContentType = "application/json";
    [ObservableProperty] private string? _bodyFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private int _selectedMethodIndex;
    [ObservableProperty] private int _selectedBodyModeIndex = (int)RequestBodyMode.Raw;
    [ObservableProperty] private RequestAuth _auth = new() { Type = AuthType.Inherit };
    [ObservableProperty] private int _selectedAuthTypeIndex;
    [ObservableProperty] private int _selectedOAuthGrantTypeIndex;
    [ObservableProperty] private bool _isPasswordVisible;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _timeoutSeconds = 30;
    [ObservableProperty] private bool _followRedirects = true;
    [ObservableProperty] private string _proxyUrl = string.Empty;
    [ObservableProperty] private bool _ignoreCertificateErrors;

    [ObservableProperty] private string _responseStatus = "No response";
    [ObservableProperty] private string _responseTime = "—";
    [ObservableProperty] private string _responseSize = "—";
    [ObservableProperty] private string _responseBody = string.Empty;
    [ObservableProperty] private bool _isPrettyResponse = true;
    private string _prettyResponseBody = string.Empty;
    public string DisplayedResponseBody => IsPrettyResponse ? _prettyResponseBody : ResponseBody;
    public bool HasInvisibleResponseCharacters => ResponseBodyFormatter.ContainsFormatCharacters(ResponseBody);
    public bool IsRawResponse { get => !IsPrettyResponse; set => IsPrettyResponse = !value; }
    partial void OnIsPrettyResponseChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRawResponse));
        OnPropertyChanged(nameof(DisplayedResponseBody));
    }
    partial void OnResponseBodyChanged(string value)
    {
        _prettyResponseBody = ResponseBodyFormatter.Pretty(value, ResponseContentType);
        OnPropertyChanged(nameof(DisplayedResponseBody));
        OnPropertyChanged(nameof(HasInvisibleResponseCharacters));
    }
    [ObservableProperty] private byte[] _responseBytes = [];
    [ObservableProperty] private string? _responseActionMessage;
    [ObservableProperty] private bool _responseBodyWasTruncated;
    [ObservableProperty] private string _responseContentType = "text/plain";
    [ObservableProperty] private string _responseFinalUrl = string.Empty;
    [ObservableProperty] private ResponseStatusClass _responseStatusClass;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private long _downloadedBytes;
    [ObservableProperty] private bool _lastSendSucceeded;

    public RequestAssertionsViewModel AssertionTests { get; }

    public ObservableCollection<KeyValueItem> QueryParams { get; } = new();
    public ObservableCollection<KeyValueItem> RequestHeaders { get; } = new();
    public string DefaultHeadersDescription => DefaultRequestHeaders.Description;
    public ObservableCollection<KeyValueItem> FormData { get; } = new();
    public ObservableCollection<KeyValueItem> ResponseHeaders { get; } = new();
    public ObservableCollection<RedirectHop> ResponseRedirects { get; } = new();
    public ObservableCollection<CookieItem> ResponseCookies { get; } = new();

    public bool HasResponseBytes => ResponseBytes.Length > 0;
    public bool HasTextResponse => HasResponseBytes && IsTextContent(ResponseContentType);
    public bool CanSaveResponse => HasResponseBytes && !ResponseBodyWasTruncated;
    public bool HasRedirects => ResponseRedirects.Count > 0;
    public bool HasCookies => ResponseCookies.Count > 0;
    public bool CanDownloadResponse => LastSendSucceeded && !IsDownloading;
    public string DownloadStatusText => IsDownloading ? $"Downloading… {FormatSize(DownloadedBytes)}" : string.Empty;

    public char PasswordChar => IsPasswordVisible ? '\0' : '●';

    public RequestTabViewModel() : this(null, null, null, null, null, null, null, null) { }
    public RequestTabViewModel(IRequestExecutionService requestExecutionService) : this(null, requestExecutionService, null, null, null, null, null, null) { }
    public RequestTabViewModel(
        IRequestExecutionService requestExecutionService,
        Func<RequestEnvironment?>? environmentProvider,
        Func<IReadOnlyList<ProductionTarget>, Task<bool>>? confirmProductionSend = null,
        IOAuthTokenService? oauthTokenService = null,
        Action<HistoryEntry>? onHistoryRecorded = null,
        ICookieJarService? cookieJarService = null,
        ICurlGenerator? curlGenerator = null)
        : this(null, requestExecutionService, environmentProvider, confirmProductionSend, oauthTokenService, onHistoryRecorded, cookieJarService, curlGenerator) { }

    private RequestTabViewModel(
        RequestItem? request,
        IRequestExecutionService? requestExecutionService,
        Func<RequestEnvironment?>? environmentProvider,
        Func<IReadOnlyList<ProductionTarget>, Task<bool>>? confirmProductionSend,
        IOAuthTokenService? oauthTokenService,
        Action<HistoryEntry>? onHistoryRecorded,
        ICookieJarService? cookieJarService,
        ICurlGenerator? curlGenerator)
    {
        AssertionTests = new RequestAssertionsViewModel(MarkDirty);
        _requestExecutionService = requestExecutionService;
        _environmentProvider = environmentProvider;
        _confirmProductionSend = confirmProductionSend;
        _onHistoryRecorded = onHistoryRecorded;
        _cookieJarService = cookieJarService;
        _curlGenerator = curlGenerator ?? new CurlGenerator();
        _queryEditor = new RequestQueryEditor(QueryParams, value => RequestUrl = value, MarkDirty);
        ConfigureEditorCollection(RequestHeaders);
        ConfigureEditorCollection(FormData);
        ObserveAuth(Auth);

        if (request is null) EnsureEmptyRows(); else Load(request);
        Summary = new RequestEditorSummary(this, () => _environmentProvider?.Invoke());
        Response = new ResponseSummary(this);
        AuthEditor = new(Auth, oauthTokenService, () => {
            var snapshot = CreateSnapshot();
            var collection = CollectionProvider?.Invoke();
            snapshot.FolderId = collection?.Requests.FirstOrDefault(item => item.Id == RequestId)?.FolderId;
            return EffectiveAuthResolver.Resolve(snapshot, collection);
        });
        AuthEditor.EnvironmentIdsProvider = () => new(_environmentProvider?.Invoke()?.Id, CollectionProvider?.Invoke()?.ActiveEnvironment?.Id);
        AuthEditor.VariableScopesProvider = () => BuildVariables(_environmentProvider?.Invoke());
        _trackingChanges = true;
        IsDirty = false;
    }

    public static RequestTabViewModel FromRequest(
        RequestItem request,
        IRequestExecutionService? requestExecutionService = null,
        Func<RequestEnvironment?>? environmentProvider = null,
        Func<IReadOnlyList<ProductionTarget>, Task<bool>>? confirmProductionSend = null,
        IOAuthTokenService? oauthTokenService = null,
        Action<HistoryEntry>? onHistoryRecorded = null,
        ICookieJarService? cookieJarService = null,
        ICurlGenerator? curlGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RequestTabViewModel(request, requestExecutionService, environmentProvider, confirmProductionSend, oauthTokenService, onHistoryRecorded, cookieJarService, curlGenerator);
    }

    public RequestItem CreateSnapshot(Guid? id = null) => new()
    {
        Id = id ?? RequestId ?? Guid.NewGuid(),
        Name = Name.Trim(),
        Method = new HttpMethod(Method),
        Url = _queryEditor.BaseUrl,
        QueryParams = CopyRows(QueryParams),
        Headers = CopyRows(RequestHeaders),
        Assertions = AssertionTests.CreateSnapshot(),
        PreRequestScript = AssertionTests.PreRequestScript,
        TestScript = AssertionTests.TestScript,
        BodyMode = BodyMode,
        BodyContentType = BodyContentType,
        Body = RequestBody,
        FormData = CopyRows(FormData),
        BodyFilePath = BodyFilePath,
        TimeoutSeconds = TimeoutSeconds,
        FollowRedirects = FollowRedirects,
        ProxyUrl = ProxyUrl,
        IgnoreCertificateErrors = IgnoreCertificateErrors,
        Auth = CopyAuth(Auth)
    };

    public void MarkSaved(Guid requestId)
    {
        RequestId = requestId;
        IsDirty = false;
    }

    private RequestItem CreateExecutionSnapshot()
    {
        var snapshot = CreateSnapshot();
        var collection = CollectionProvider?.Invoke();
        snapshot.FolderId = collection?.Requests.FirstOrDefault(request => request.Id == RequestId)?.FolderId;
        snapshot.Auth = EffectiveAuthResolver.Resolve(snapshot, collection).Auth.Clone();
        return snapshot;
    }

    /// <summary>Takes the name of a request renamed elsewhere (the sidebar) without touching the
    /// dirty state: the rename is already saved, and any other unsaved edits stay unsaved.</summary>
    public void ApplySavedName(string name)
    {
        var wasTracking = _trackingChanges;
        _trackingChanges = false;
        Name = name;
        _trackingChanges = wasTracking;
    }

    /// <summary>
    /// Builds a runnable curl command for the request as currently edited, without sending it -
    /// resolving {{variables}} against whichever environment is currently selected. Pure/testable;
    /// the view's code-behind is responsible for actually writing the result to the clipboard.
    /// </summary>
    public string BuildCurlCommand(bool revealSecrets)
    {
        var snapshot = CreateExecutionSnapshot();
        var scopes = BuildVariables(_environmentProvider?.Invoke());
        var command = _curlGenerator.Generate(snapshot, scopes.Variables, revealSecrets);
        return revealSecrets ? command : HistoryRedactor.Redact(command,
            scopes.SecretNames.Where(scopes.Variables.ContainsKey).Select(name => scopes.Variables[name])
                .Concat(CreateScriptScopes(_environmentProvider?.Invoke()).SecretValues()).ToArray());
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    /// <summary>Clears the shared, workspace-lifetime cookie jar - not just this tab's cookie
    /// list, which is only a per-send display projection of it.</summary>
    [RelayCommand]
    private void ClearCookies()
    {
        CookiePersistenceWarning = _cookieJarService is null ? null :
            PersistenceFailures.Try(_cookieJarService.Clear) && _cookieJarService.PersistenceWarning is null ? null : ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Cookies.PersistenceWarning");
        ResponseCookies.Clear();
        OnPropertyChanged(nameof(HasCookies));
    }

    [RelayCommand]
    private void RemoveRow(KeyValueItem? item)
    {
        if (item is null) return;
        var collection = QueryParams.Contains(item) ? QueryParams : RequestHeaders.Contains(item) ? RequestHeaders : FormData.Contains(item) ? FormData : null;
        if (collection is null) return;
        collection.Remove(item);
        EnsureEmptyRow(collection);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsSending) return;
        AssertionTests.NotEvaluated();
        if (_requestExecutionService is null)
        {
            SetError("Request execution is not configured.");
            return;
        }

        if (TimeoutSeconds <= 0)
        {
            SetError(ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.Request.TimeoutInvalid"));
            return;
        }

        var environment = _environmentProvider?.Invoke();
        var owner = CollectionProvider?.Invoke();
        var productionTargets = ProductionTarget.Collect(environment, owner?.ActiveEnvironment, owner?.Name);
        var executionTargets = productionTargets.Select(target => target.Snapshot()).ToArray();
        if (productionTargets.Count > 0 && _confirmProductionSend is not null)
        {
            var proceed = await _confirmProductionSend(productionTargets);
            if (!proceed)
            {
                SetResponseActionMessage("Send canceled: target environment is marked production.");
                return;
            }
        }

        _sendCancellationSource = new CancellationTokenSource();
        IsSending = true;
        ResponseStatus = "Sending…";
        ResponseStatusClass = ResponseStatusClass.Sending;

        ResponseActionMessage = null;
        try
        {
            var snapshot = CreateExecutionSnapshot();
            var scopes = BuildVariables(environment);
            var variables = scopes.Variables;
            var unresolvedNames = CollectUnresolvedVariableNames(snapshot, variables);
            if (unresolvedNames.Count > 0)
            {
                ResponseActionMessage = $"Unresolved variable{(unresolvedNames.Count == 1 ? "" : "s")}: " +
                    string.Join(", ", unresolvedNames.Select(name => $"{{{{{name}}}}}"));
            }

            var executionOptions = new RequestExecutionOptions
            {
                EnvironmentId = environment?.Id, CollectionEnvironmentId = CollectionProvider?.Invoke()?.ActiveEnvironment?.Id,
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
                FollowRedirects = FollowRedirects,
                ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl,
                IgnoreCertificateErrors = IgnoreCertificateErrors,
                Variables = variables,
                SecretVariableNames = scopes.SecretNames,
                ScriptScopes = CreateScriptScopes(environment, SaveScopes)
            };
            var run = await new RequestRunner(_requestExecutionService).RunAsync(snapshot, executionOptions, _sendCancellationSource.Token);
            RunCompleted?.Invoke(snapshot, run);
            var result = run.Execution;
            AssertionTests.Apply(run);
            RecordHistory(snapshot, result, run.Scripts.SelectMany(script => script.SecretValues).ToArray());

            if (!result.IsSuccess)
            {
                LastSendSucceeded = false;
                SetError(result.ErrorMessage ?? result.FailureKind.ToString(), result.Elapsed);
                ResponseStatus = ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Failure." + result.FailureKind);
                return;
            }

            _lastReplay = run.Replay;
            _lastProductionTargets = executionTargets;
            LastSendSucceeded = true;

            ResponseHeaders.Clear();
            foreach (var header in result.Headers)
            {
                ResponseHeaders.Add(new KeyValueItem { Key = header.Key, Value = header.Value });
            }

            ResponseRedirects.Clear();
            foreach (var hop in result.RedirectHistory)
            {
                ResponseRedirects.Add(hop);
            }
            OnPropertyChanged(nameof(HasRedirects));

            ResponseCookies.Clear();
            if (_cookieJarService is not null && result.FinalUri is not null)
            {
                foreach (System.Net.Cookie cookie in _cookieJarService.Container.GetCookies(result.FinalUri))
                {
                    ResponseCookies.Add(CookieItem.FromCookie(cookie));
                }

                CookiePersistenceWarning = PersistenceFailures.Try(_cookieJarService.Save) && _cookieJarService.PersistenceWarning is null ? null : ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Cookies.PersistenceWarning");
            }
            OnPropertyChanged(nameof(HasCookies));

            ResponseStatus = $"{(int)result.StatusCode!} {result.ReasonPhrase}";
            ResponseBodyWasTruncated = result.BodyWasTruncated;
            ResponseStatusClass = ResponseStatusClassifier.FromStatusCode((int)result.StatusCode!);
            ResponseTime = $"{result.Elapsed.TotalMilliseconds:0} ms";
            ResponseContentType = result.ContentType;
            ResponseBytes = result.Body;
            ResponseSize = result.ContentLength is long contentLength ? FormatSize(contentLength) : FormatSize(result.Body.LongLength);
            ResponseFinalUrl = result.FinalUri?.AbsoluteUri ?? string.Empty;
            if (result.BodyWasTruncated)
            {
                ResponseSize += $" (preview capped at {FormatSize(result.Body.LongLength)})";
            }

            ResponseBody = IsTextContent(result.ContentType)
                ? DecodeBody(result.Body, result.CharacterSet)
                : $"Binary response preview is unavailable. {FormatSize(result.Body.LongLength)} received.";
        }
        finally
        {
            _sendCancellationSource.Dispose();
            _sendCancellationSource = null;
            IsSending = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsSending))]
    private void Cancel() => _sendCancellationSource?.Cancel();

    public Task<bool> SaveResponseAsync(Stream destination) =>
        ResponseBodyWriter.SaveAsync(ResponseBytes, ResponseBodyWasTruncated, destination);

    public async Task<bool> DownloadResponseAsync(string destinationPath, Func<string, Task<bool>>? confirmRepeat = null)
    {
        if (_requestExecutionService is null || _lastReplay is null || !LastSendSucceeded || IsDownloading)
        {
            SetResponseActionMessage(AppLocalization.Get("Str.Response.DownloadRequiresSend"));
            return false;
        }

        _downloadCancellationSource = new CancellationTokenSource();
        IsDownloading = true;
        DownloadedBytes = 0;
        var replay = _lastReplay;
        var productionTargets = _lastProductionTargets;
        var request = replay.CreateRequest();
        var progress = new Progress<long>(bytes => DownloadedBytes = bytes);
        try
        {
            if (request.Method.Method.ToUpperInvariant() is not ("GET" or "HEAD" or "OPTIONS") &&
                (confirmRepeat is null || !await confirmRepeat(request.Method.Method)))
            {
                SetResponseActionMessage(AppLocalization.Get("Str.Response.DownloadCanceled"));
                return false;
            }
            if (productionTargets.Count > 0 && _confirmProductionSend is not null &&
                !await _confirmProductionSend(productionTargets))
            {
                SetResponseActionMessage(AppLocalization.Get("Str.Response.DownloadCanceled"));
                return false;
            }
            var result = await _requestExecutionService.DownloadAsync(
                request, destinationPath, replay.CreateOptions(), progress, _downloadCancellationSource.Token);

            SetResponseActionMessage(result.IsSuccess
                ? string.Format(AppLocalization.Get("Str.Response.DownloadSucceeded"), FormatSize(result.BytesWritten), Path.GetFileName(destinationPath))
                : string.Format(AppLocalization.Get("Str.Response.DownloadFailed"), HistoryRedactor.Redact(result.ErrorMessage ?? "", result.ResolvedSecretValues)));
            return result.IsSuccess;
        }
        finally
        {
            _downloadCancellationSource.Dispose();
            _downloadCancellationSource = null;
            IsDownloading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDownloading))]
    private void CancelDownload() => _downloadCancellationSource?.Cancel();

    partial void OnMethodChanged(string value) => MarkDirty();
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnRequestUrlChanged(string value) => _queryEditor.SetUrl(value);
    partial void OnRequestBodyChanged(string value) => MarkDirty();
    public bool IsRawBody => BodyMode == RequestBodyMode.Raw;
    public bool IsFormBody => BodyMode is RequestBodyMode.FormData or RequestBodyMode.UrlEncoded;
    public bool IsBinaryBody => BodyMode == RequestBodyMode.Binary;
    partial void OnBodyModeChanged(RequestBodyMode value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(IsRawBody));
        OnPropertyChanged(nameof(IsFormBody));
        OnPropertyChanged(nameof(IsBinaryBody));
        OnPropertyChanged(nameof(RequestBodyIncompleteNotice));
    }
    partial void OnBodyContentTypeChanged(string value) => MarkDirty();
    partial void OnBodyFilePathChanged(string? value) => MarkDirty();
    partial void OnIsPasswordVisibleChanged(bool value) => OnPropertyChanged(nameof(PasswordChar));
    partial void OnIsSendingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();
    partial void OnIsDownloadingChanged(bool value)
    {
        CancelDownloadCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDownloadResponse));
        OnPropertyChanged(nameof(DownloadStatusText));
    }
    partial void OnDownloadedBytesChanged(long value) => OnPropertyChanged(nameof(DownloadStatusText));
    partial void OnLastSendSucceededChanged(bool value) => OnPropertyChanged(nameof(CanDownloadResponse));

    partial void OnFollowRedirectsChanged(bool value) => MarkDirty();
    partial void OnTimeoutSecondsChanged(int value) => MarkDirty();
    partial void OnProxyUrlChanged(string value) => MarkDirty();
    partial void OnIgnoreCertificateErrorsChanged(bool value) => MarkDirty();
    partial void OnResponseBytesChanged(byte[] value) { OnPropertyChanged(nameof(HasResponseBytes)); OnPropertyChanged(nameof(HasTextResponse)); OnPropertyChanged(nameof(CanSaveResponse)); }
    partial void OnResponseContentTypeChanged(string value)
    {
        OnPropertyChanged(nameof(HasTextResponse));
        OnResponseBodyChanged(ResponseBody);
    }
    partial void OnResponseBodyWasTruncatedChanged(bool value) => OnPropertyChanged(nameof(CanSaveResponse));
    partial void OnSelectedMethodIndexChanged(int value)
    {
        if (value >= 0 && value < Methods.Length) Method = Methods[value];
    }

    partial void OnSelectedBodyModeIndexChanged(int value)
    {
        if (Enum.IsDefined(typeof(RequestBodyMode), value)) BodyMode = (RequestBodyMode)value;
    }

    partial void OnSelectedAuthTypeIndexChanged(int value)
    {
        if (Enum.IsDefined(typeof(AuthType), value)) Auth.Type = (AuthType)value;
    }

    partial void OnSelectedOAuthGrantTypeIndexChanged(int value)
    {
        if (Enum.IsDefined(typeof(OAuthGrantType), value)) Auth.GrantType = (OAuthGrantType)value;
    }

    partial void OnAuthChanged(RequestAuth value)
    {
        if (AuthEditor is not null) AuthEditor.Auth = value;
        ObserveAuth(value);
        SelectedAuthTypeIndex = (int)value.Type;
        SelectedOAuthGrantTypeIndex = (int)value.GrantType;
        MarkDirty();

    }

    public void SetResponseActionMessage(string? message) => ResponseActionMessage = message;

    private void Load(RequestItem request)
    {
        request = request.CloneForEditing();
        RequestQueryString.Normalize(request);
        _trackingChanges = false;
        RequestId = request.Id;
        Name = request.Name;
        Method = request.Method.Method;
        RequestUrl = request.Url;
        BodyMode = request.BodyMode;
        BodyContentType = request.BodyContentType;
        RequestBody = request.Body;
        BodyFilePath = request.BodyFilePath;
        TimeoutSeconds = request.TimeoutSeconds;
        FollowRedirects = request.FollowRedirects;
        ProxyUrl = request.ProxyUrl;
        IgnoreCertificateErrors = request.IgnoreCertificateErrors;
        Auth = CopyAuth(request.Auth);
        AssertionTests.Load(request.Assertions, request.PreRequestScript, request.TestScript);
        CopyRowsInto(request.QueryParams, QueryParams);
        CopyRowsInto(request.Headers, RequestHeaders);
        CopyRowsInto(request.FormData, FormData);
        SelectedMethodIndex = Math.Max(0, Array.IndexOf(Methods, Method.ToUpperInvariant()));
        SelectedBodyModeIndex = (int)BodyMode;
        SelectedAuthTypeIndex = (int)Auth.Type;
        SelectedOAuthGrantTypeIndex = (int)Auth.GrantType;
        EnsureEmptyRows();
    }

    private void ConfigureEditorCollection(ObservableCollection<KeyValueItem> collection) => collection.CollectionChanged += (_, args) =>
    {
        if (args.NewItems is not null) foreach (KeyValueItem item in args.NewItems) item.PropertyChanged += OnRowPropertyChanged;
        if (args.OldItems is not null) foreach (KeyValueItem item in args.OldItems) item.PropertyChanged -= OnRowPropertyChanged;
        MarkDirty();
    };

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is KeyValueItem item && (args.PropertyName == nameof(KeyValueItem.Key) || args.PropertyName == nameof(KeyValueItem.Value)))
        {
            if (QueryParams.Contains(item)) EnsureEmptyRow(QueryParams);
            if (RequestHeaders.Contains(item)) EnsureEmptyRow(RequestHeaders);
            if (FormData.Contains(item)) EnsureEmptyRow(FormData);
        }
        MarkDirty();
    }

    private void ObserveAuth(RequestAuth auth)
    {
        if (_observedAuth is not null) _observedAuth.PropertyChanged -= OnAuthPropertyChanged;
        _observedAuth = auth;
        _observedAuth.PropertyChanged += OnAuthPropertyChanged;
    }

    private void OnAuthPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is RequestAuth auth)
        {
            if (args.PropertyName == nameof(RequestAuth.Type)) SelectedAuthTypeIndex = (int)auth.Type;
            if (args.PropertyName == nameof(RequestAuth.GrantType)) SelectedOAuthGrantTypeIndex = (int)auth.GrantType;
        }
        MarkDirty();
    }

    private void EnsureEmptyRows()
    {
        EnsureEmptyRow(QueryParams);
        EnsureEmptyRow(RequestHeaders);
        EnsureEmptyRow(FormData);
    }

    private void EnsureEmptyRow(ObservableCollection<KeyValueItem> rows)
    {
        if (!rows.Any(row => !IsPersistableRow(row))) rows.Add(new KeyValueItem());
    }

    private static ObservableCollection<KeyValueItem> CopyRows(ObservableCollection<KeyValueItem> rows) => new(rows.Where(IsPersistableRow).Select(CopyRow));
    private static void CopyRowsInto(ObservableCollection<KeyValueItem>? source, ObservableCollection<KeyValueItem> destination)
    {
        destination.Clear();
        if (source is not null) foreach (var row in source.Where(IsPersistableRow)) destination.Add(CopyRow(row));
    }
    private static KeyValueItem CopyRow(KeyValueItem row) => new() { IsEnabled = row.IsEnabled, Key = row.Key, Value = row.Value, Description = row.Description, FieldType = row.FieldType, FilePath = row.FilePath, UseRawQueryText = row.UseRawQueryText, HasQueryEquals = row.HasQueryEquals };
    private static RequestAuth CopyAuth(RequestAuth auth) => auth.Clone();
    private static bool IsPersistableRow(KeyValueItem row) => row.UseRawQueryText || !string.IsNullOrWhiteSpace(row.Key) || !string.IsNullOrWhiteSpace(row.Value) || !string.IsNullOrWhiteSpace(row.Description) || row.IsFile || !string.IsNullOrWhiteSpace(row.FilePath);

    private static List<string> CollectUnresolvedVariableNames(RequestItem request, IReadOnlyDictionary<string, string> variables)
    {
        var names = new List<string>();
        RequestVariableTemplates.Transform(request, value =>
        {
            foreach (var name in Resolver.Resolve(value, variables).UnresolvedVariableNames)
            {
                if (!names.Contains(name)) names.Add(name);
            }
            return value ?? "";
        });

        return names;
    }

    /// <summary>
    /// Fires the (optional) history hook once per send, success or failure. RequestSnapshot is
    /// the pre-resolution <paramref name="snapshot"/> - {{variable}} tokens intact, never the
    /// literal resolved request. Known session values are removed from the snapshot; the response side
    /// can't avoid real values, so headers/body/error are redacted via ResolvedSecretValues
    /// before ever being assigned, never stored raw and redacted later.
    /// </summary>
    private void RecordHistory(RequestItem snapshot, RequestExecutionResult result, IReadOnlyList<string> scriptSecrets)
    {
        if (_onHistoryRecorded is null) return;
        var secrets = result.ResolvedSecretValues.Concat(scriptSecrets).ToArray();

        var preview = ResponsePreview.Create(result, secrets, MaxHistoryBodyPreviewBytes);

        // Redact before capping so a secret straddling the cut can't leave a readable prefix behind.
        var requestSnapshot = SessionSecrets.RedactSnapshot(snapshot, secrets);
        (requestSnapshot.Body, var requestBodyWasTruncated) = HistoryRequestBody.Cap(requestSnapshot.Body);

        var entry = new HistoryEntry
        {
            SourceRequestId = RequestId,
            SentAtUtc = DateTimeOffset.UtcNow,
            RequestSnapshot = requestSnapshot,
            RequestBodyWasTruncatedForHistory = requestBodyWasTruncated,
            StatusCode = (int?)result.StatusCode,
            ReasonPhrase = result.ReasonPhrase is null ? null : HistoryRedactor.Redact(result.ReasonPhrase, secrets),
            RedactedResponseHeadersText = preview.HeadersText,
            RedactedResponseBodyPreview = preview.BodyPreview,
            ResponseBodyWasTruncatedForHistory = preview.BodyWasTruncated,
            Elapsed = result.Elapsed,
            ResponseSize = result.IsSuccess ? result.ContentLength ?? result.Body.LongLength : null,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage is null ? null : HistoryRedactor.Redact(result.ErrorMessage, secrets)
        };

        _onHistoryRecorded(entry);
        _hasRecordedHistory = true;
        RefreshHistoryWarning();
    }

    private static string DecodeBody(byte[] bytes, string? characterSet)
    {
        try { return Encoding.GetEncoding(string.IsNullOrWhiteSpace(characterSet) ? "utf-8" : characterSet).GetString(bytes); }
        catch (ArgumentException) { return Encoding.UTF8.GetString(bytes); }
    }
    private static bool IsTextContent(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase);

    private static string FormatSize(long byteCount) => byteCount < 1024 * 1000
        ? $"{byteCount / 1024.0:F2} KB"
        : $"{byteCount / 1024.0 / 1024.0:F2} MB";
    private void SetError(string message, TimeSpan elapsed = default)
    {
        ResponseStatus = "Error";
        ResponseBodyWasTruncated = false;
        ResponseStatusClass = ResponseStatusClass.TransportError;
        ResponseBody = message;
        ResponseTime = elapsed == default ? "0 ms" : $"{elapsed.TotalMilliseconds:0} ms";
        ResponseBytes = [];
        ResponseSize = "0 KB";
        ResponseHeaders.Clear();
        ResponseRedirects.Clear();
        OnPropertyChanged(nameof(HasRedirects));
        ResponseCookies.Clear();
        OnPropertyChanged(nameof(HasCookies));
    }
    private void MarkDirty() { if (_trackingChanges) IsDirty = true; Summary?.Recount(); }
}
