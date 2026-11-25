using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>
/// Persists issued OAuth tokens outside the workspace file, addressed by the opaque
/// cache key stored on a RequestAuth. Values are protected via ISecretStore; a store that
/// cannot protect a value must fail rather than persist it in plaintext.
/// </summary>
public interface IOAuthTokenCache
{
    Task<OAuthToken?> GetAsync(string cacheKey, CancellationToken cancellationToken = default, OAuthContext? context = null);
    Task SetAsync(string cacheKey, OAuthToken token, CancellationToken cancellationToken = default);
    Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Removes only the entry for one resolved context, keeping other contexts under the key.</summary>
    Task RemoveAsync(string cacheKey, OAuthContext context, CancellationToken cancellationToken = default);
}
