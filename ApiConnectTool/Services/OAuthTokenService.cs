using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public sealed class OAuthTokenService(IHttpClientFactory httpClientFactory, IOAuthTokenCache tokenCache) : IOAuthTokenService
{
    private const string HttpClientName = "ApiConnectTool.Http";
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(3);

    public async Task<string> GetValidAccessTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        auth = auth.Clone();

        OAuthAuthValidation.ValidateTemplates(auth);

        var context = OAuthContext.From(auth);
        var cached = await tokenCache.GetAsync(auth.OAuthCacheKey, cancellationToken, context).ConfigureAwait(false);
        if (cached is not null && cached.ExpiresAtUtc > DateTimeOffset.UtcNow + ExpiryBuffer)
        {
            return cached.AccessToken;
        }

        if (!string.IsNullOrEmpty(cached?.RefreshToken))
        {
            try
            {
                var refreshed = await RefreshTokenAsync(auth, cached.RefreshToken, cancellationToken).ConfigureAwait(false);
                await tokenCache.SetAsync(auth.OAuthCacheKey, refreshed, cancellationToken).ConfigureAwait(false);
                return refreshed.AccessToken;
            }
            catch (OAuthTokenRejectedException rejection) when (rejection.IsGrantRejection)
            {
                // The server refused this refresh token; retrying it on every send cannot succeed.
                // Network failures and timeouts are not caught here, so the entry survives them.
                await tokenCache.RemoveAsync(auth.OAuthCacheKey, context, cancellationToken).ConfigureAwait(false);
                if (auth.GrantType != OAuthGrantType.ClientCredentials)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        DefaultStrings.Get("Str.OAuth.RefreshRejected"), rejection.Message), rejection);
                }
            }
        }

        if (auth.GrantType == OAuthGrantType.ClientCredentials)
        {
            var token = await RequestClientCredentialsTokenAsync(auth, cancellationToken).ConfigureAwait(false);
            await tokenCache.SetAsync(auth.OAuthCacheKey, token, cancellationToken).ConfigureAwait(false);
            return token.AccessToken;
        }

        throw new InvalidOperationException("OAuth token expired or missing. Re-authenticate using the Authorize button.");
    }

    public Task ClearCachedTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        OAuthAuthValidation.ValidateTemplates(auth);
        return tokenCache.RemoveAsync(auth.OAuthCacheKey, OAuthContext.From(auth), cancellationToken);
    }

    public Task<OAuthToken> RequestClientCredentialsTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        OAuthAuthValidation.ValidateTemplates(auth);
        if (string.IsNullOrWhiteSpace(auth.ClientId))
        {
            throw new InvalidOperationException("OAuth2 Client Credentials auth requires a client ID.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.ClientId,
            ["client_secret"] = auth.ClientSecret.Value
        };
        if (!string.IsNullOrWhiteSpace(auth.Scope))
        {
            form["scope"] = auth.Scope;
        }

        return RequestTokenAsync(auth, form, fallbackRefreshToken: null, cancellationToken);
    }

    public Task<OAuthToken> RefreshTokenAsync(RequestAuth auth, string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        OAuthAuthValidation.ValidateTemplates(auth);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = auth.ClientId
        };
        if (!string.IsNullOrWhiteSpace(auth.ClientSecret.Value))
        {
            form["client_secret"] = auth.ClientSecret.Value;
        }

        return RequestTokenAsync(auth, form, fallbackRefreshToken: refreshToken, cancellationToken);
    }

    public async Task<OAuthToken> AuthorizeWithPkceAsync(RequestAuth auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        auth = auth.Clone();
        OAuthAuthValidation.ValidateTemplates(auth);
        if (string.IsNullOrWhiteSpace(auth.AuthorizationUrl))
        {
            throw new InvalidOperationException("OAuth2 Authorization Code auth requires an authorization URL.");
        }
        if (string.IsNullOrWhiteSpace(auth.ClientId))
        {
            throw new InvalidOperationException("OAuth2 Authorization Code auth requires a client ID.");
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var state = GenerateCodeVerifier();

        using var listener = new HttpListener();
        listener.Prefixes.Add(auth.RedirectUri.TrimEnd('/') + "/");
        listener.Start();

        try
        {
            var authorizeUrl = BuildAuthorizationUrl(auth, codeChallenge, state);
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            using var timeoutSource = new CancellationTokenSource(AuthorizationTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var getContextTask = listener.GetContextAsync();
            await using (linkedSource.Token.Register(() => listener.Stop()))
            {
                HttpListenerContext context;
                try
                {
                    context = await getContextTask.ConfigureAwait(false);
                }
                catch (Exception) when (linkedSource.IsCancellationRequested)
                {
                    throw new InvalidOperationException("OAuth authorization timed out or was canceled.");
                }

                var query = context.Request.QueryString;
                var code = query["code"];
                var returnedState = query["state"];

                WriteCallbackResponse(context.Response, code is not null && returnedState == state);

                if (returnedState != state)
                {
                    throw new InvalidOperationException("OAuth authorization response failed state validation.");
                }
                if (string.IsNullOrEmpty(code))
                {
                    throw new InvalidOperationException(DefaultStrings.Get("Str.OAuth.NoCode"));
                }

                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = auth.RedirectUri,
                    ["client_id"] = auth.ClientId,
                    ["code_verifier"] = codeVerifier
                };
                if (!string.IsNullOrWhiteSpace(auth.ClientSecret.Value))
                {
                    form["client_secret"] = auth.ClientSecret.Value;
                }

                var token = await RequestTokenAsync(auth, form, fallbackRefreshToken: null, cancellationToken).ConfigureAwait(false);
                await tokenCache.SetAsync(auth.OAuthCacheKey, token, cancellationToken).ConfigureAwait(false);
                return token;
            }
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }
    }

    private static string BuildAuthorizationUrl(RequestAuth auth, string codeChallenge, string state)
    {
        var builder = new UriBuilder(auth.AuthorizationUrl);
        var query = string.IsNullOrEmpty(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        query += $"response_type=code&client_id={Uri.EscapeDataString(auth.ClientId)}" +
                 $"&redirect_uri={Uri.EscapeDataString(auth.RedirectUri)}" +
                 $"&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256" +
                 $"&state={Uri.EscapeDataString(state)}";
        if (!string.IsNullOrWhiteSpace(auth.Scope))
        {
            query += $"&scope={Uri.EscapeDataString(auth.Scope)}";
        }
        builder.Query = query;
        return builder.Uri.AbsoluteUri;
    }

    private static void WriteCallbackResponse(HttpListenerResponse response, bool succeeded)
    {
        var body = succeeded
            ? "Authorization complete. You can close this tab and return to ApiConnectTool."
            : "Authorization failed. You can close this tab and return to ApiConnectTool.";
        var bytes = Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>");
        response.ContentType = "text/html";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private async Task<OAuthToken> RequestTokenAsync(RequestAuth auth, Dictionary<string, string> form,
        string? fallbackRefreshToken, CancellationToken cancellationToken)
    {
        try
        {
            return await RequestTokenCoreAsync(auth, form, fallbackRefreshToken, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(DefaultStrings.Get("Str.OAuth.ConnectionFailed"));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(DefaultStrings.Get("Str.OAuth.InvalidResponse"));
        }
        catch (UriFormatException)
        {
            throw new InvalidOperationException(DefaultStrings.Get("Str.OAuth.InvalidUrl"));
        }
    }

    private async Task<OAuthToken> RequestTokenCoreAsync(
        RequestAuth auth,
        Dictionary<string, string> form,
        string? fallbackRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.TokenUrl))
        {
            throw new InvalidOperationException("OAuth2 auth requires a token URL.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var context = OAuthContext.From(auth);
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(auth.TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthTokenRejectedException((int)response.StatusCode,
                OAuthTokenResponse.FormatFailure((int)response.StatusCode, responseBody, auth, form));
        }

        try
        {
            return OAuthTokenResponse.Parse(responseBody, fallbackRefreshToken, context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(DefaultStrings.Get("Str.OAuth.InvalidResponse"), exception);
        }
    }

    public static string GenerateCodeVerifier()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    public static string GenerateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
