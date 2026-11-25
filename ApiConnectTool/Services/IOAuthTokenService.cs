using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IOAuthTokenService
{
    /// <summary>
    /// Returns a currently-valid access token for <paramref name="auth"/>, using a cached
    /// token if unexpired, transparently refreshing it if a refresh token is available, or
    /// (for Client Credentials only, since no user interaction is required) requesting a
    /// fresh one. Throws InvalidOperationException if no valid token can be obtained without
    /// interactive re-authentication.
    /// </summary>
    Task<string> GetValidAccessTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default);

    Task<OAuthToken> RequestClientCredentialsTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default);

    Task<OAuthToken> RefreshTokenAsync(RequestAuth auth, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the interactive Authorization Code + PKCE flow: opens the system browser to the
    /// authorization URL and waits for the loopback redirect. Not automatable - manual
    /// smoke-test item, invoked only from an explicit user "Authorize" action.
    /// </summary>
    Task<OAuthToken> AuthorizeWithPkceAsync(RequestAuth auth, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached token for <paramref name="auth"/>'s resolved context only. Callers pass
    /// auth that is already resolved and environment-bound, exactly as for Authorize, so tokens
    /// for other environments or configurations under the same cache key are kept.
    /// </summary>
    Task ClearCachedTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default);
}
