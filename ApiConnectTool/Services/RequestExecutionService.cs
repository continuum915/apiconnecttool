using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public sealed class RequestExecutionService(
    IHttpClientFactory httpClientFactory,
    IVariableResolver? variableResolver = null,
    IOAuthTokenService? oauthTokenService = null,
    ICookieJarService? cookieJarService = null) : IRequestExecutionService
{
    private const string HttpClientName = "ApiConnectTool.Http";
    private const int MaxRedirectHops = 20;
    private const int StreamChunkBytes = 81920;
    private static readonly IReadOnlyDictionary<string, string> EmptyVariables = new Dictionary<string, string>();
    private static readonly IReadOnlySet<string> EmptySecretVariableNames = new HashSet<string>();

    private readonly IVariableResolver _variableResolver = variableResolver ?? new VariableResolver();

    private static readonly HttpStatusCode[] RedirectStatusCodes =
    [
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Found,
        HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect
    ];

    public async Task<RequestExecutionResult> ExecuteAsync(
        RequestItem requestDefinition,
        RequestExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestDefinition);
        options ??= new RequestExecutionOptions();

        if (options.Timeout <= TimeSpan.Zero)
        {
            return RequestExecutionResult.Failed(RequestExecutionFailureKind.Validation, "Timeout must be greater than zero.");
        }

        var secretValues = new HashSet<string>();
        requestDefinition = ResolveVariables(requestDefinition, options.Variables ?? EmptyVariables, options.SecretVariableNames ?? EmptySecretVariableNames, secretValues);
        new OAuthEnvironmentIds(options.EnvironmentId, options.CollectionEnvironmentId).ApplyTo(requestDefinition.Auth);
        CollectAuthSecrets(requestDefinition.Auth, secretValues);

        if (!TryParseUrl(requestDefinition.Url, out var initialUri))
        {
            return RequestExecutionResult.Failed(RequestExecutionFailureKind.Validation, "Enter an absolute HTTP or HTTPS URL.", resolvedSecretValues: secretValues.ToList());
        }
        if (options.MaxResponseBytes <= 0)
        {
            return RequestExecutionResult.Failed(RequestExecutionFailureKind.Validation, "Response preview limit must be greater than zero.", resolvedSecretValues: secretValues.ToList());
        }
        if (!string.IsNullOrWhiteSpace(options.ProxyUrl) && !IsValidProxyUrl(options.ProxyUrl, out _))
        {
            return RequestExecutionResult.Failed(RequestExecutionFailureKind.Validation, "Enter an absolute HTTP or HTTPS proxy URL (e.g. http://host:port).", resolvedSecretValues: secretValues.ToList());
        }

        using var timeoutSource = new CancellationTokenSource(options.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var stopwatch = Stopwatch.StartNew();
        SentRequest? sent = null;

        try
        {
            MultipartFileContent.Validate(requestDefinition);
            requestDefinition = await ResolveOAuthAsync(requestDefinition, secretValues, linkedSource.Token).ConfigureAwait(false);
            var (response, hops, finalUri, ownedClient) = await SendWithRedirectsAsync(requestDefinition, initialUri, options, secretValues, linkedSource.Token,
                request => sent = request).ConfigureAwait(false);
            try
            {
                var responseBody = await ReadResponseBodyAsync(response.Content, options.MaxResponseBytes, linkedSource.Token).ConfigureAwait(false);
                stopwatch.Stop();

                var headers = response.Headers.Concat(response.Content.Headers)
                    .Select(header => new KeyValueItem { Key = header.Key, Value = string.Join(", ", header.Value) })
                    .ToList();

                return new RequestExecutionResult
                {
                    IsSuccess = true,
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase,
                    Elapsed = stopwatch.Elapsed,
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                    CharacterSet = response.Content.Headers.ContentType?.CharSet,
                    Body = responseBody.Body,
                    ContentLength = response.Content.Headers.ContentLength,
                    BodyWasTruncated = responseBody.WasTruncated,
                    Headers = headers,
                    FinalUri = finalUri,
                    RedirectHistory = hops,
                    ResolvedSecretValues = secretValues.ToList(),
                    SentRequest = sent
                };
            }
            finally
            {
                response.Dispose();
                ownedClient?.Dispose();
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var (kind, message) = ClassifyException(ex, cancellationToken, options.Timeout);
            var failed = RequestExecutionResult.Failed(kind, message, stopwatch.Elapsed, secretValues.ToList());
            failed.SentRequest = sent;
            return failed;
        }
    }

    public async Task<RequestDownloadResult> DownloadAsync(
        RequestItem requestDefinition,
        string destinationPath,
        RequestExecutionOptions? options = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        options ??= new RequestExecutionOptions();

        if (options.Timeout <= TimeSpan.Zero)
        {
            return RequestDownloadResult.Failed(RequestExecutionFailureKind.Validation, "Timeout must be greater than zero.");
        }

        var secretValues = new HashSet<string>();
        requestDefinition = ResolveVariables(requestDefinition, options.Variables ?? EmptyVariables, options.SecretVariableNames ?? EmptySecretVariableNames, secretValues);
        new OAuthEnvironmentIds(options.EnvironmentId, options.CollectionEnvironmentId).ApplyTo(requestDefinition.Auth);
        CollectAuthSecrets(requestDefinition.Auth, secretValues);

        if (!TryParseUrl(requestDefinition.Url, out var initialUri))
        {
            return RequestDownloadResult.Failed(RequestExecutionFailureKind.Validation, "Enter an absolute HTTP or HTTPS URL.", resolvedSecretValues: secretValues.ToList());
        }
        if (!string.IsNullOrWhiteSpace(options.ProxyUrl) && !IsValidProxyUrl(options.ProxyUrl, out _))
        {
            return RequestDownloadResult.Failed(RequestExecutionFailureKind.Validation, "Enter an absolute HTTP or HTTPS proxy URL (e.g. http://host:port).", resolvedSecretValues: secretValues.ToList());
        }

        using var timeoutSource = new CancellationTokenSource(options.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var stopwatch = Stopwatch.StartNew();
        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".apiconnecttoolpart";

        try
        {
            MultipartFileContent.Validate(requestDefinition);
            requestDefinition = await ResolveOAuthAsync(requestDefinition, secretValues, linkedSource.Token).ConfigureAwait(false);
            var (response, hops, finalUri, ownedClient) = await SendWithRedirectsAsync(requestDefinition, initialUri, options, secretValues, linkedSource.Token).ConfigureAwait(false);
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    return new RequestDownloadResult
                    {
                        FailureKind = RequestExecutionFailureKind.Validation,
                        ErrorMessage = string.Format(Localization.DefaultStrings.Get("Str.Response.DownloadHttpFailure"), (int)response.StatusCode),
                        StatusCode = response.StatusCode, Elapsed = stopwatch.Elapsed,
                        FinalUri = finalUri, RedirectHistory = hops, ResolvedSecretValues = secretValues.ToList()
                    };
                }
                // Headers arrived: from here the timeout is an idle limit that resets after every
                // chunk, so a long download that keeps receiving data never times out.
                timeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);
                linkedSource.Token.ThrowIfCancellationRequested();
                long bytesWritten;
                using (var idleSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    try
                    {
                        bytesWritten = await StreamToFileAsync(response.Content, tempPath, progress, idleSource, options.Timeout).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        TryDeleteFile(tempPath);
                        return RequestDownloadResult.Failed(RequestExecutionFailureKind.Timeout,
                            string.Format(Localization.DefaultStrings.Get("Str.Response.DownloadStalled"), options.Timeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.CurrentCulture)),
                            stopwatch.Elapsed, secretValues.ToList());
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, destinationPath, overwrite: true);
                stopwatch.Stop();

                return new RequestDownloadResult
                {
                    IsSuccess = true,
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase,
                    Elapsed = stopwatch.Elapsed,
                    BytesWritten = bytesWritten,
                    FinalUri = finalUri,
                    RedirectHistory = hops,
                    ResolvedSecretValues = secretValues.ToList()
                };
            }
            finally
            {
                response.Dispose();
                ownedClient?.Dispose();
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TryDeleteFile(tempPath);
            var (kind, message) = ClassifyException(ex, cancellationToken, options.Timeout);
            return RequestDownloadResult.Failed(kind, message, stopwatch.Elapsed, secretValues.ToList());
        }
    }

    /// <summary>
    /// Substitutes {{name}} tokens across the URL, query params, headers, auth fields, and body
    /// using the resolved environment variable map. Never mutates <paramref name="definition"/> -
    /// execution must operate on an immutable snapshot. Unresolved tokens are left as literal
    /// {{name}} text so a bad send is visibly wrong rather than silently blank.
    /// </summary>
    private RequestItem ResolveVariables(RequestItem definition, IReadOnlyDictionary<string, string> variables, IReadOnlySet<string> secretVariableNames, HashSet<string> secretValuesOut)
    {
        // Always work on a copy: the OAuth binding below is written to it, never to the caller's request.
        string Resolve(string? value)
        {
            var result = _variableResolver.Resolve(value, variables);
            foreach (var name in result.ResolvedVariableNames)
            {
                if (secretVariableNames.Contains(name) && variables.TryGetValue(name, out var secretValue) && !string.IsNullOrEmpty(secretValue))
                {
                    secretValuesOut.Add(secretValue);
                }
            }

            return result.Value;
        }
        return RequestVariableTemplates.Transform(definition, Resolve);
    }

    /// <summary>
    /// Collects the literal secret values carried directly on a resolved (post-variable-substitution)
    /// auth block - Bearer/API-key token, Basic password/encoded credential, and OAuth2 client secret - into the
    /// per-send secret set. Called before any OAuth swap so a client-credentials request's own
    /// client secret is captured even though it never appears on the outgoing request itself.
    /// </summary>
    private static void CollectAuthSecrets(RequestAuth auth, HashSet<string> secretValuesOut)
    {
        if (!string.IsNullOrEmpty(auth.Token.Value)) secretValuesOut.Add(auth.Token.Value);
        if (!string.IsNullOrEmpty(auth.Password.Value)) secretValuesOut.Add(auth.Password.Value);
        if (!string.IsNullOrEmpty(auth.ClientSecret.Value)) secretValuesOut.Add(auth.ClientSecret.Value);
        if (auth.Type == AuthType.Basic && !string.IsNullOrWhiteSpace(auth.Username))
            secretValuesOut.Add(RequestSecrets.BasicCredential(auth.Username, auth.Password.Value));
    }

    /// <summary>
    /// If the request uses OAuth2 auth, resolves a valid access token (cached, refreshed, or
    /// freshly issued for Client Credentials) and swaps it in as an equivalent Bearer auth so
    /// downstream request-building logic doesn't need to know about OAuth. Never mutates
    /// <paramref name="definition"/>.
    /// </summary>
    private async Task<RequestItem> ResolveOAuthAsync(RequestItem definition, HashSet<string> secretValuesOut, CancellationToken cancellationToken)
    {
        if (definition.Auth.Type != AuthType.OAuth2 || oauthTokenService is null)
        {
            return definition;
        }

        var accessToken = await oauthTokenService.GetValidAccessTokenAsync(definition.Auth, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(accessToken)) secretValuesOut.Add(accessToken);
        return CloneWithAuth(definition, new RequestAuth { Type = AuthType.Bearer, Token = new EncryptedString(accessToken) });
    }

    private static RequestItem CloneWithAuth(RequestItem definition, RequestAuth auth) => new()
    {
        Id = definition.Id,
        Name = definition.Name,
        Method = definition.Method,
        Url = definition.Url,
        QueryParams = definition.QueryParams,
        Headers = definition.Headers,
        BodyMode = definition.BodyMode,
        BodyContentType = definition.BodyContentType,
        Body = definition.Body,
        FormData = definition.FormData,
        BodyFilePath = definition.BodyFilePath,
        Auth = auth
    };

    private static bool TryParseUrl(string? url, out Uri uri)
    {
        var parsed = Uri.TryCreate(url, UriKind.Absolute, out var candidate) &&
            (candidate!.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps);
        uri = parsed ? candidate! : null!;
        return parsed;
    }

    private static bool IsValidProxyUrl(string proxyUrl, out Uri uri) => TryParseUrl(proxyUrl, out uri);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static (RequestExecutionFailureKind Kind, string Message) ClassifyException(Exception ex, CancellationToken callerToken, TimeSpan timeout) => ex switch
    {
        OperationCanceledException when callerToken.IsCancellationRequested => (RequestExecutionFailureKind.Canceled, "Request canceled."),
        OperationCanceledException => (RequestExecutionFailureKind.Timeout, $"Request timed out after {timeout.TotalSeconds:0} seconds."),
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } httpEx => (RequestExecutionFailureKind.Tls, httpEx.Message),
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } httpEx => (RequestExecutionFailureKind.Dns, httpEx.Message),
        HttpRequestException httpEx => (RequestExecutionFailureKind.Network, httpEx.Message),
        FileNotFoundException fileEx => (RequestExecutionFailureKind.File, fileEx.Message),
        IOException ioEx => (RequestExecutionFailureKind.File, ioEx.Message),
        ArgumentException or FormatException or InvalidOperationException => (RequestExecutionFailureKind.Validation, ex.Message),
        _ => (RequestExecutionFailureKind.Unexpected, ex.Message)
    };

    private async Task<(HttpResponseMessage Response, List<RedirectHop> Hops, Uri FinalUri, HttpClient? OwnedClient)> SendWithRedirectsAsync(
        RequestItem definition, Uri initialUri, RequestExecutionOptions options, HashSet<string> secrets, CancellationToken cancellationToken,
        Action<SentRequest>? onInitialRequest = null)
    {
        var hops = new List<RedirectHop>();
        var currentUri = initialUri;
        var method = definition.Method;
        var includeBody = true;
        var retainCredentials = true;
        var (client, owned) = CreateHttpClient(options);
        var ownedClient = owned ? client : null;

        try
        {
            while (true)
            {
                using var message = CreateRequestMessage(definition, currentUri, method, includeBody, hops.Count == 0);
                if (hops.Count > 0) RedirectCredentials.Apply(message, definition.Auth, secrets, retainCredentials);
                var sentUri = message.RequestUri!;
                if (hops.Count == 0 && onInitialRequest is not null)
                    onInitialRequest(new SentRequest(message.Method.Method, sentUri,
                        message.Headers.Concat(message.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                            .Select(header => new KeyValueItem { Key = header.Key, Value = string.Join(", ", header.Value) }).ToList()));
                if (message.Headers.TryGetValues("Cookie", out var outgoingCookies))
                    secrets.UnionWith(outgoingCookies.SelectMany(SessionSecrets.FromCookieHeader));
                if (cookieJarService is not null)
                    secrets.UnionWith(cookieJarService.Container.GetCookies(sentUri).Cast<Cookie>().Select(cookie => cookie.Value));
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.Headers.TryGetValues("Set-Cookie", out var issuedCookies))
                    secrets.UnionWith(issuedCookies.Select(SessionSecrets.FromSetCookieHeader));

                if (!options.FollowRedirects ||
                    !RedirectStatusCodes.Contains(response.StatusCode) ||
                    response.Headers.Location is null)
                {
                    return (response, hops, sentUri, ownedClient);
                }

                using var redirectResponse = response;
                if (hops.Count >= MaxRedirectHops)
                {
                    throw new HttpRequestException($"Too many redirects (exceeded {MaxRedirectHops}).");
                }

                var location = response.Headers.Location;
                var nextUri = location.IsAbsoluteUri ? location : new Uri(sentUri, location);
                RedirectCredentials.ValidateDestination(sentUri, nextUri);
                retainCredentials &= RedirectCredentials.SameOrigin(sentUri, nextUri);
                hops.Add(new RedirectHop { RequestUri = sentUri, StatusCode = response.StatusCode, Location = nextUri });

                // 303 always downgrades to GET; 301/302 downgrade only non-GET/HEAD methods, matching common browser/curl behavior.
                if (response.StatusCode == HttpStatusCode.SeeOther ||
                    (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found &&
                     method != HttpMethod.Get && method != HttpMethod.Head))
                {
                    method = HttpMethod.Get;
                    includeBody = false;
                }

                currentUri = nextUri;
            }
        }
        catch { ownedClient?.Dispose(); throw; }
    }

    /// <summary>
    /// The pooled, factory-managed client covers the common case. A custom proxy or a TLS
    /// bypass needs its own handler instance (these are handler-level settings that must not
    /// leak into other concurrent requests sharing the pooled client), so those requests get a
    /// dedicated, disposable client instead.
    /// </summary>
    private (HttpClient Client, bool Owned) CreateHttpClient(RequestExecutionOptions options)
    {
        var hasProxy = !string.IsNullOrWhiteSpace(options.ProxyUrl);
        if (!hasProxy && !options.IgnoreCertificateErrors)
        {
            return (httpClientFactory.CreateClient(HttpClientName), false);
        }

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (hasProxy)
        {
            IsValidProxyUrl(options.ProxyUrl!, out var proxyUri);
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxyUri);
        }
        if (options.IgnoreCertificateErrors)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        if (cookieJarService is not null)
        {
            // Give this one-off handler the same shared jar the pooled client uses, so a
            // proxied/TLS-bypass request still participates in the workspace's single cookie
            // session instead of getting its own default (and immediately discarded) container.
            handler.UseCookies = true;
            handler.CookieContainer = cookieJarService.Container;
        }

        var client = new HttpClient(new ResponseDecompressionHandler(handler), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        return (client, true);
    }

    private static HttpRequestMessage CreateRequestMessage(RequestItem definition, Uri initialUri, HttpMethod method, bool includeBody, bool isInitialRequest)
    {
        var message = new HttpRequestMessage(method, isInitialRequest ? RequestQueryString.AppendToUri(initialUri, definition.QueryParams) : initialUri);
        try
        {
            if (isInitialRequest || definition.Auth.Type != AuthType.ApiKey || !string.IsNullOrWhiteSpace(definition.Auth.ApiKeyHeaderName))
                ApplyAuth(message, definition.Auth);
            message.Content = includeBody ? CreateContent(definition) : null;

            foreach (var header in definition.Headers.Where(IsEnabledWithKey))
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(DefaultStrings.Get("Str.Request.ContentLengthComputed"));

                // Request headers refuse content headers (Content-Type, Content-Encoding,
                // Content-Disposition, Expires, ...); those belong on the body.
                var isContentType = header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase);
                if (!isContentType && message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    continue;
                if (!isContentType && !IsContentHeader(header.Key))
                    throw new InvalidOperationException($"Header '{header.Key}' is not valid for this request.");

                if (message.Content is null)
                {
                    // A redirect hop that dropped the body drops its content headers with it.
                    if (!includeBody) continue;
                    throw new InvalidOperationException(string.Format(DefaultStrings.Get("Str.Request.ContentHeaderRequiresBody"), header.Key));
                }

                if (isContentType)
                    message.Content.Headers.ContentType = ApplyUserContentType(definition.BodyMode, message.Content.Headers.ContentType, header.Value);
                else if (!message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    throw new InvalidOperationException($"Header '{header.Key}' is not valid for this request.");
            }

            DefaultRequestHeaders.Apply(message);
            return message;
        }
        catch
        {
            message.Dispose();
            throw;
        }
    }

    private static bool IsContentHeader(string name)
    {
        using var probe = new ByteArrayContent([]);
        return probe.Headers.TryAddWithoutValidation(name, string.Empty);
    }

    /// <summary>
    /// A user Content-Type replaces the generated one, except that a multipart media type on a
    /// form-data body keeps the generated boundary: the body was written with it, so any other
    /// boundary (or none) would make the body unparseable.
    /// </summary>
    private static MediaTypeHeaderValue ApplyUserContentType(RequestBodyMode bodyMode, MediaTypeHeaderValue? generated, string userValue)
    {
        var user = MediaTypeHeaderValue.Parse(userValue);
        var boundary = generated?.Parameters.FirstOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
        if (bodyMode != RequestBodyMode.FormData || boundary is null
            || user.MediaType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) != true)
            return user;

        foreach (var existing in user.Parameters.Where(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)).ToArray())
            user.Parameters.Remove(existing);
        user.Parameters.Add(new NameValueHeaderValue(boundary.Name, boundary.Value));
        return user;
    }

    private static HttpContent? CreateContent(RequestItem definition)
    {
        if (definition.Method == HttpMethod.Get || definition.Method == HttpMethod.Head || definition.BodyMode == RequestBodyMode.None)
        {
            return null;
        }

        return definition.BodyMode switch
        {
            RequestBodyMode.Raw => new StringContent(definition.Body ?? string.Empty, ResolveEncoding(definition.BodyContentType), definition.BodyContentType),
            RequestBodyMode.UrlEncoded => new FormUrlEncodedContent(definition.FormData.Where(row => IsEnabledWithKey(row) && !row.IsFile).Select(row => new KeyValuePair<string, string>(row.Key, row.Value))),
            RequestBodyMode.FormData => MultipartFileContent.Create(definition.FormData),
            RequestBodyMode.Binary => CreateBinaryContent(definition.BodyFilePath, definition.BodyContentType),
            _ => null
        };
    }

    private static HttpContent CreateBinaryContent(string? bodyFilePath, string contentType)
    {
        if (string.IsNullOrWhiteSpace(bodyFilePath))
        {
            throw new FileNotFoundException("Choose a file for the binary request body.");
        }

        var content = new StreamContent(File.OpenRead(bodyFilePath));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        return content;
    }

    /// <summary>
    /// Applies auth or throws InvalidOperationException (classified as a Validation failure by
    /// ClassifyException) for an incomplete/conflicting configuration - a request must never go
    /// out silently missing the auth its type says it has.
    /// </summary>
    private static void ApplyAuth(HttpRequestMessage message, RequestAuth auth)
    {
        switch (auth.Type)
        {
            case AuthType.None:
                break;
            case AuthType.Bearer:
                if (string.IsNullOrWhiteSpace(auth.Token.Value))
                {
                    throw new InvalidOperationException("Bearer auth requires a token.");
                }
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token.Value);
                break;
            case AuthType.Basic:
                if (string.IsNullOrWhiteSpace(auth.Username))
                {
                    throw new InvalidOperationException("Basic auth requires a username.");
                }
                var credential = RequestSecrets.BasicCredential(auth.Username, auth.Password.Value);
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
                break;
            case AuthType.ApiKey:
                if (string.IsNullOrWhiteSpace(auth.ApiKeyHeaderName) && string.IsNullOrWhiteSpace(auth.ApiKeyQueryParam))
                {
                    throw new InvalidOperationException("API key auth requires a header name or query parameter.");
                }
                if (string.IsNullOrWhiteSpace(auth.Token.Value))
                {
                    throw new InvalidOperationException("API key auth requires a value.");
                }
                if (!string.IsNullOrWhiteSpace(auth.ApiKeyHeaderName))
                {
                    message.Headers.TryAddWithoutValidation(auth.ApiKeyHeaderName, auth.Token.Value);
                }
                else
                {
                    var uri = message.RequestUri!;
                    var separator = string.IsNullOrEmpty(uri.Query) ? string.Empty : uri.Query.TrimStart('?') + "&";
                    var builder = new UriBuilder(uri) { Query = separator + Uri.EscapeDataString(auth.ApiKeyQueryParam) + "=" + Uri.EscapeDataString(auth.Token.Value) };
                    message.RequestUri = builder.Uri;
                }
                break;
        }
    }

    private static Encoding ResolveEncoding(string? contentType)
    {
        try
        {
            var charset = MediaTypeHeaderValue.Parse(contentType ?? "text/plain").CharSet;
            return string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        }
        catch (FormatException)
        {
            return Encoding.UTF8;
        }
    }

    private static bool IsEnabledWithKey(KeyValueItem row) => row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key);

    private static async Task<(byte[] Body, bool WasTruncated)> ReadResponseBodyAsync(HttpContent content, int maxResponseBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, StreamChunkBytes));
        var chunk = new byte[Math.Min(maxResponseBytes, StreamChunkBytes)];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return (buffer.ToArray(), false);
            }

            var remaining = maxResponseBytes - (int)buffer.Length;
            if (read > remaining)
            {
                buffer.Write(chunk, 0, remaining);
                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length == maxResponseBytes)
            {
                var extraByte = await stream.ReadAsync(chunk.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                return (buffer.ToArray(), extraByte > 0);
            }
        }
    }

    /// <summary>Streams the body to <paramref name="destinationPath"/>. <paramref name="idleSource"/>
    /// is cancelled when no data arrives for <paramref name="idleTimeout"/>; the timer restarts
    /// after every chunk, so the total duration is unbounded.</summary>
    private static async Task<long> StreamToFileAsync(HttpContent content, string destinationPath, IProgress<long>? progress,
        CancellationTokenSource idleSource, TimeSpan idleTimeout)
    {
        var cancellationToken = idleSource.Token;
        idleSource.CancelAfter(idleTimeout);
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamChunkBytes, useAsync: true);

        var buffer = new byte[StreamChunkBytes];
        long bytesWritten = 0;
        while (true)
        {
            idleSource.CancelAfter(idleTimeout);
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            idleSource.CancelAfter(Timeout.InfiniteTimeSpan);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            bytesWritten += read;
            progress?.Report(bytesWritten);
        }

        return bytesWritten;
    }
}
