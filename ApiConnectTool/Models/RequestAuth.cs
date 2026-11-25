using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Models;

public enum AuthType
{
    None,
    Bearer,
    Basic,
    ApiKey,
    OAuth2,
    Inherit
}

public partial class RequestAuth : ObservableObject
{
    [JsonIgnore] public Guid? OAuthEnvironmentId { get; set; }
    [JsonIgnore] public Guid? OAuthCollectionEnvironmentId { get; set; }
    internal bool AllowInheritance { get; set; } = true;
    partial void OnTypeChanging(AuthType value)
    {
        if (value == AuthType.Inherit && !AllowInheritance)
            throw new ArgumentException("Collection authorization cannot inherit.", nameof(value));
    }

    public RequestAuth Clone(bool preserveCacheKey = true) => new()
    {
        OAuthEnvironmentId = OAuthEnvironmentId,
        OAuthCollectionEnvironmentId = OAuthCollectionEnvironmentId,
        Type = Type, Token = Token, Username = Username, Password = Password,
        ApiKeyHeaderName = ApiKeyHeaderName, ApiKeyQueryParam = ApiKeyQueryParam,
        ClientId = ClientId, ClientSecret = ClientSecret, AuthorizationUrl = AuthorizationUrl,
        TokenUrl = TokenUrl, Scope = Scope, RedirectUri = RedirectUri, GrantType = GrantType,
        OAuthCacheKey = preserveCacheKey ? OAuthCacheKey : Guid.NewGuid().ToString("N")
    };
    [ObservableProperty] private AuthType _type = AuthType.None;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TokenText))] private EncryptedString _token = new(string.Empty);
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PasswordText))] private EncryptedString _password = new(string.Empty);
    [ObservableProperty] private string _apiKeyHeaderName = string.Empty;
    [ObservableProperty] private string _apiKeyQueryParam = string.Empty;

    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientSecretText))] private EncryptedString _clientSecret = new(string.Empty);
    [JsonIgnore] public string TokenText { get => Token.Value; set { if (value != Token.Value) Token = value; } }
    [JsonIgnore] public string PasswordText { get => Password.Value; set { if (value != Password.Value) Password = value; } }
    [JsonIgnore] public string ClientSecretText { get => ClientSecret.Value; set { if (value != ClientSecret.Value) ClientSecret = value; } }
    [ObservableProperty] private string _authorizationUrl = string.Empty;
    [ObservableProperty] private string _tokenUrl = string.Empty;
    [ObservableProperty] private string _scope = string.Empty;
    [ObservableProperty] private string _redirectUri = "http://127.0.0.1";
    [ObservableProperty] private OAuthGrantType _grantType = OAuthGrantType.AuthorizationCodePkce;

    /// <summary>
    /// Lookup key for this auth's cached OAuth token in the OS-secret-store-backed token
    /// cache. Not itself a secret - it lets a saved request "remember" it has a token without
    /// the token living in workspace JSON. Stable per RequestAuth instance so re-saving a
    /// request doesn't orphan its cached token.
    /// </summary>
    [ObservableProperty] private string _oAuthCacheKey = Guid.NewGuid().ToString("N");
}
