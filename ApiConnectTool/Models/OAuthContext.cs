using System;

namespace ApiConnectTool.Models;

// Contains resolved credentials: persist only through the secret-store boundary.
public sealed record OAuthContext(Guid? EnvironmentId, OAuthGrantType Grant, string AuthorizationUrl,
    string TokenUrl, string ClientId, string ClientSecret, string Scope, string RedirectUri)
{
    /// <summary>The active collection environment the token was issued under, alongside
    /// <see cref="EnvironmentId"/> (the active global environment). Tokens cached before collection
    /// environments existed deserialize with null here, so they still match when no collection
    /// environment is active and simply miss the cache otherwise.</summary>
    public Guid? CollectionEnvironmentId { get; init; }

    public static OAuthContext From(RequestAuth auth) => new(auth.OAuthEnvironmentId, auth.GrantType,
        auth.AuthorizationUrl, auth.TokenUrl, auth.ClientId, auth.ClientSecret.Value, auth.Scope, auth.RedirectUri)
        { CollectionEnvironmentId = auth.OAuthCollectionEnvironmentId };

    public override string ToString() => "OAuth context (protected)";
}

/// <summary>The environment pair an OAuth token is bound to: the active global environment and the
/// active environment of the request's collection. Either may be null.</summary>
public readonly record struct OAuthEnvironmentIds(Guid? GlobalEnvironmentId, Guid? CollectionEnvironmentId)
{
    public void ApplyTo(RequestAuth auth)
    {
        auth.OAuthEnvironmentId = GlobalEnvironmentId;
        auth.OAuthCollectionEnvironmentId = CollectionEnvironmentId;
    }
}
