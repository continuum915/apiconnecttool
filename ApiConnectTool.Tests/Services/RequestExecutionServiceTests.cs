using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class RequestExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_InvalidUrl_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.ExecuteAsync(new RequestItem { Url = "not-a-url" });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_RawRequest_ForwardsHeadersQueryAndBody()
    {
        HttpRequestMessage? captured = null;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
        {
            captured = await CloneAsync(request);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                ReasonPhrase = "Created",
                Content = new StringContent("{\"id\":1}", Encoding.UTF8, "application/json")
            };
        })));
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://example.test/pets?existing=true",
            Body = "{\"name\":\"Mochi\"}",
            Headers = new() { new KeyValueItem { Key = "X-Trace", Value = "trace-1" } },
            QueryParams = new() { new KeyValueItem { Key = "dryRun", Value = "true" } }
        };

        var result = await service.ExecuteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal("application/json", result.ContentType);
        Assert.NotNull(captured);
        Assert.Equal("existing=true&dryRun=true", captured!.RequestUri!.Query.TrimStart('?'));
        Assert.Equal("trace-1", captured.Headers.GetValues("X-Trace").Single());
        Assert.Equal("{\"name\":\"Mochi\"}", await captured.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithVariables_SubstitutesUrlHeadersAndBodyBeforeSending()
    {
        HttpRequestMessage? captured = null;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
        {
            captured = await CloneAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "{{baseUrl}}/pets",
            Body = "{\"owner\":\"{{owner}}\"}",
            Headers = new() { new KeyValueItem { Key = "X-Trace", Value = "{{traceId}}" } }
        };
        var variables = new Dictionary<string, string>
        {
            ["baseUrl"] = "https://example.test",
            ["owner"] = "Mochi",
            ["traceId"] = "trace-1"
        };

        var result = await service.ExecuteAsync(request, new RequestExecutionOptions { Variables = variables });

        Assert.True(result.IsSuccess);
        Assert.Equal("https://example.test/pets", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal("trace-1", captured.Headers.GetValues("X-Trace").Single());
        Assert.Equal("{\"owner\":\"Mochi\"}", await captured.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedVariable_IsLeftLiteralAndFailsUrlValidation()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send when the URL is still unresolved."))));

        var result = await service.ExecuteAsync(new RequestItem { Url = "{{missing}}/pets" });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_BearerAuthWithoutToken_ReturnsValidationFailureInsteadOfSendingUnauthenticated()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send when auth is misconfigured."))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("Bearer", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_BasicAuthWithoutUsername_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send when auth is misconfigured."))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Basic, Password = "secret" }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("Basic", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApiKeyAuthWithoutHeaderOrQueryParam_ReturnsValidationFailureInsteadOfSendingUnauthenticated()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send when auth is misconfigured."))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.ApiKey, Token = "secret" }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("API key", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApiKeyAuthWithoutValue_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send when auth is misconfigured."))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.ApiKey, ApiKeyHeaderName = "X-API-Key" }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("API key", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApiKeyAuthViaQueryParam_StillSucceeds()
    {
        HttpRequestMessage? captured = null;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test/pets",
            Auth = new RequestAuth { Type = AuthType.ApiKey, ApiKeyQueryParam = "api_key", Token = "secret" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("api_key=secret", captured!.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ExecuteAsync_OAuth2Auth_AttachesAccessTokenAsBearerHeader()
    {
        HttpRequestMessage? captured = null;
        var service = new RequestExecutionService(
            new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
            {
                captured = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            variableResolver: null,
            oauthTokenService: new FakeOAuthTokenService(auth => Task.FromResult("issued-access-token")));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test/pets",
            Auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c", TokenUrl = "https://auth.example.test/token" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("issued-access-token", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ExecuteAsync_OAuth2AuthWithNoTokenServiceConfigured_TreatsAsUnauthenticated()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test/pets",
            Auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c", TokenUrl = "https://auth.example.test/token" }
        });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_OAuth2AuthCannotObtainToken_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(
            new TestHttpClientFactory(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Must not send without a token."))),
            variableResolver: null,
            oauthTokenService: new FakeOAuthTokenService(_ => throw new InvalidOperationException("OAuth token expired or missing. Re-authenticate using the Authorize button.")));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test/pets",
            Auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce, ClientId = "c", TokenUrl = "https://auth.example.test/token" }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("re-authenticate", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UrlEncodedBody_UsesFormContent()
    {
        HttpRequestMessage? captured = null;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
        {
            captured = await CloneAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://example.test/token",
            BodyMode = RequestBodyMode.UrlEncoded,
            FormData = new() { new KeyValueItem { Key = "grant_type", Value = "client_credentials" } }
        };

        var result = await service.ExecuteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("application/x-www-form-urlencoded", captured!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("grant_type=client_credentials", await captured.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ResponseExceedsPreviewLimit_ReturnsBoundedBody()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("abcdef"));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        })));

        var result = await service.ExecuteAsync(
            new RequestItem { Url = "https://example.test" },
            new RequestExecutionOptions { MaxResponseBytes = 4 });

        Assert.True(result.IsSuccess);
        Assert.Equal("abcd", Encoding.UTF8.GetString(result.Body));
        Assert.Equal(6, result.ContentLength);
        Assert.True(result.BodyWasTruncated);
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvableHost_ReturnsDnsFailure()
    {
        // "*.invalid" is reserved by RFC 2606 to never resolve, so this is deterministic
        // without depending on reaching any real public endpoint.
        var service = new RequestExecutionService(new RealNetworkHttpClientFactory());

        var result = await service.ExecuteAsync(new RequestItem { Url = "http://apiconnecttool-dns-test-host.invalid/" });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Dns, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_ReturnsCanceledFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async _ =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ExecuteAsync(new RequestItem { Url = "https://example.test" }, cancellationToken: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Canceled, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_FollowsRedirectAndRecordsHopHistory()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/a")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("/b", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("final", Encoding.UTF8, "text/plain")
            });
        })));

        var result = await service.ExecuteAsync(new RequestItem { Url = "https://example.test/a" });

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("https://example.test/b", result.FinalUri!.AbsoluteUri);
        Assert.Single(result.RedirectHistory);
        Assert.Equal("https://example.test/a", result.RedirectHistory[0].RequestUri.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Found, result.RedirectHistory[0].StatusCode);
        Assert.Equal("https://example.test/b", result.RedirectHistory[0].Location!.AbsoluteUri);
    }

    [Fact]
    public async Task ExecuteAsync_FollowRedirectsDisabled_ReturnsRedirectResponseDirectly()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://example.test/b");
            return Task.FromResult(redirect);
        })));

        var result = await service.ExecuteAsync(
            new RequestItem { Url = "https://example.test/a" },
            new RequestExecutionOptions { FollowRedirects = false });

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        Assert.Empty(result.RedirectHistory);
    }

    [Fact]
    public async Task ExecuteAsync_PostRedirectedByFound_DowngradesToGetAndDropsBody()
    {
        var capturedRequests = new List<HttpRequestMessage>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
        {
            capturedRequests.Add(await CloneAsync(request));
            if (request.RequestUri!.AbsolutePath == "/a")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://example.test/b");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://example.test/a",
            Body = "{}"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, capturedRequests.Count);
        Assert.Equal(HttpMethod.Get, capturedRequests[1].Method);
        Assert.Null(capturedRequests[1].Content);
    }

    [Fact]
    public async Task ExecuteAsync_TooManyRedirects_ReturnsNetworkFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(request.RequestUri!.AbsolutePath == "/a" ? "/b" : "/a", UriKind.Relative);
            return Task.FromResult(redirect);
        })));

        var result = await service.ExecuteAsync(new RequestItem { Url = "https://example.test/a" });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Network, result.FailureKind);
        Assert.Contains("Too many redirects", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_NoSecrets_ResolvedSecretValuesIsEmpty()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.ExecuteAsync(new RequestItem { Url = "https://example.test" });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ResolvedSecretValues);
    }

    [Fact]
    public async Task ExecuteAsync_BearerAuth_ResolvedSecretValuesContainsExactlyTheToken()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(["top-secret-token"], result.ResolvedSecretValues);
    }

    [Fact]
    public async Task ExecuteAsync_BasicAuth_ResolvedSecretValuesContainsPasswordAndCredentialButNotUsername()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test",
            Auth = new RequestAuth { Type = AuthType.Basic, Username = "alice", Password = "hunter2" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(["hunter2", "YWxpY2U6aHVudGVyMg=="], result.ResolvedSecretValues);
        Assert.DoesNotContain("alice", result.ResolvedSecretValues);
    }

    [Fact]
    public async Task ExecuteAsync_OAuth2Auth_ResolvedSecretValuesContainsIssuedAccessToken()
    {
        var service = new RequestExecutionService(
            new TestHttpClientFactory(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))),
            variableResolver: null,
            oauthTokenService: new FakeOAuthTokenService(_ => Task.FromResult("issued-access-token")));

        var result = await service.ExecuteAsync(new RequestItem
        {
            Url = "https://example.test/pets",
            Auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c", TokenUrl = "https://auth.example.test/token" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(["issued-access-token"], result.ResolvedSecretValues);
    }

    [Fact]
    public async Task ExecuteAsync_SecretVariableSubstituted_ResolvedSecretValuesContainsItsValueButNotNonSecretVariables()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var request = new RequestItem
        {
            Url = "{{baseUrl}}/pets",
            Headers = new() { new KeyValueItem { Key = "X-Api-Key", Value = "{{apiKey}}" } }
        };
        var options = new RequestExecutionOptions
        {
            Variables = new Dictionary<string, string> { ["baseUrl"] = "https://example.test", ["apiKey"] = "sekret-value" },
            SecretVariableNames = new HashSet<string> { "apiKey" }
        };

        var result = await service.ExecuteAsync(request, options);

        Assert.True(result.IsSuccess);
        Assert.Equal(["sekret-value"], result.ResolvedSecretValues);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFailureAfterVariableResolution_StillPopulatesResolvedSecretValues()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("Must not send: max response bytes is invalid."))));
        var options = new RequestExecutionOptions
        {
            MaxResponseBytes = 0,
            Variables = new Dictionary<string, string> { ["apiKey"] = "sekret-value" },
            SecretVariableNames = new HashSet<string> { "apiKey" }
        };

        var result = await service.ExecuteAsync(
            new RequestItem { Url = "https://example.test?key={{apiKey}}" },
            options);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Equal(["sekret-value"], result.ResolvedSecretValues);
    }

    [Fact]
    public async Task DownloadAsync_StreamsResponseBodyDirectlyToFile()
    {
        var payload = new byte[512 * 1024];
        new Random(42).NextBytes(payload);
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }))));

        var destination = Path.Combine(Path.GetTempPath(), $"apiconnecttool-download-test-{Guid.NewGuid():N}.bin");
        try
        {
            var progressReports = new List<long>();
            var progress = new Progress<long>(progressReports.Add);

            var result = await service.DownloadAsync(new RequestItem { Url = "https://example.test/file" }, destination, progress: progress);

            Assert.True(result.IsSuccess);
            Assert.Equal(payload.LongLength, result.BytesWritten);
            Assert.True(File.Exists(destination));
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".apiconnecttoolpart"));
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    [Fact]
    public async Task DownloadAsync_InvalidUrl_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var result = await service.DownloadAsync(new RequestItem { Url = "not-a-url" }, Path.Combine(Path.GetTempPath(), "unused.bin"));

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidProxyUrl_ReturnsValidationFailure()
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("The pooled client must not be used when proxy validation fails."))));

        var result = await service.ExecuteAsync(
            new RequestItem { Url = "https://example.test" },
            new RequestExecutionOptions { ProxyUrl = "not a url" });

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_WithProxyUrl_RoutesThroughConfiguredProxyInsteadOfPooledClient()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = Task.Run(async () =>
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync();
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

            var body = "proxied"u8.ToArray();
            var responseText = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var responseBytes = Encoding.ASCII.GetBytes(responseText);
            await stream.WriteAsync(responseBytes);
            await stream.WriteAsync(body);
            return requestLine;
        });

        // A pooled-client factory that throws proves this request never touched IHttpClientFactory:
        // proxy configuration must create its own dedicated handler instead.
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("The pooled client must not be used for a proxied request."))));

        var result = await service.ExecuteAsync(
            new RequestItem { Url = "http://apiconnecttool-proxy-test.invalid/widgets" },
            new RequestExecutionOptions { ProxyUrl = $"http://127.0.0.1:{port}" });

        var requestLine = await acceptTask;
        listener.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal("proxied", Encoding.UTF8.GetString(result.Body));
        Assert.StartsWith("GET http://apiconnecttool-proxy-test.invalid/widgets", requestLine);
    }

    [Fact]
    public async Task ExecuteAsync_SelfSignedCertificate_FailsByDefaultAndSucceedsWhenExplicitlyBypassed()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // The default (blocked) call has no bypass requested, so it legitimately uses the pooled
        // client and must perform real certificate validation against a real TLS server.
        var service = new RequestExecutionService(new RealNetworkHttpClientFactory());

        var firstServe = ServeOneTlsRequestAsync(listener, certificate);
        var blockedResult = await service.ExecuteAsync(new RequestItem { Url = $"https://127.0.0.1:{port}/" });
        await firstServe;

        Assert.False(blockedResult.IsSuccess);
        Assert.Equal(RequestExecutionFailureKind.Tls, blockedResult.FailureKind);

        var secondServe = ServeOneTlsRequestAsync(listener, certificate);
        var bypassedResult = await service.ExecuteAsync(
            new RequestItem { Url = $"https://127.0.0.1:{port}/" },
            new RequestExecutionOptions { IgnoreCertificateErrors = true });
        await secondServe;
        listener.Stop();

        Assert.True(bypassedResult.IsSuccess);
        Assert.Equal("secure", Encoding.UTF8.GetString(bypassedResult.Body));
    }

    private static async Task ServeOneTlsRequestAsync(TcpListener listener, X509Certificate2 certificate)
    {
        try
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            using var networkStream = tcpClient.GetStream();
            using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.None, false);

            using var reader = new StreamReader(sslStream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

            var body = "secure"u8.ToArray();
            var responseText = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await sslStream.WriteAsync(Encoding.ASCII.GetBytes(responseText));
            await sslStream.WriteAsync(body);
            await sslStream.FlushAsync();
        }
        catch (Exception)
        {
            // The client is expected to abort the handshake when certificate validation is not bypassed.
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=apiconnecttool-tls-test", ecdsa, HashAlgorithmName.SHA256);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
        {
            clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync());
            foreach (var header in request.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private sealed class RealNetworkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler { AllowAutoRedirect = false });
    }

    private sealed class FakeOAuthTokenService(Func<RequestAuth, Task<string>> getValidAccessToken) : IOAuthTokenService
    {
        public Task<string> GetValidAccessTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => getValidAccessToken(auth);
        public Task<OAuthToken> RequestClientCredentialsTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> RefreshTokenAsync(RequestAuth auth, string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> AuthorizeWithPkceAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearCachedTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
