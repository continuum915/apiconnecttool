using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public sealed class AuthorizePreparationTests
{
    [Theory]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    [InlineData("TokenUrl")]
    [InlineData("AuthorizationUrl")]
    [InlineData("RedirectUri")]
    [InlineData("Scope")]
    public async Task UnresolvedOAuthFieldsBlockInteractivePreparation(string field)
    {
        var auth = InteractiveAuth();
        auth.ClientId = "client";
        auth.ClientSecret = "secret";
        auth.TokenUrl = "https://auth.test/token";
        auth.AuthorizationUrl = "https://auth.test/authorize";
        auth.RedirectUri = "http://127.0.0.1:12345/";
        auth.Scope = "read";
        typeof(RequestAuth).GetProperty(field)!.SetValue(auth,
            field == "ClientSecret" ? new EncryptedString("{{missing}}") : "{{missing}}");
        var oauth = new PreparedOAuth();
        using var editor = new AuthEditorViewModel(auth, oauth);
        await editor.AuthorizeCommand.ExecuteAsync(null);
        Assert.Null(oauth.Prepared);
        Assert.Contains("variable", editor.AuthorizationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{missing}}", editor.AuthorizationMessage);
        Assert.False(editor.IsAuthorizing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InteractivePreparationResolvesEveryContextFieldAndConcealsDerivedSecrets(bool inherited)
    {
        var auth = InteractiveAuth();
        var environment = new RequestEnvironment();
        foreach (var (key, value) in new Dictionary<string, string> {
            ["client"] = "interactive-client", ["secret"] = "private &\"value",
            ["authorize"] = "https://auth.test/authorize", ["token"] = "https://auth.test/token",
            ["scope"] = "read write", ["redirect"] = "http://127.0.0.1:12345/" })
            environment.Variables.Add(new() { Key = key, InitialValue = value, IsSecret = key == "secret" });
        var oauth = new PreparedOAuth { Failure = "private &\"value private%20%26%22value private+%26%22value private \\u0026\\u0022value" };
        var workspace = new Workspace();
        workspace.GlobalVariables.Add(new() { Key = "client", InitialValue = "global-client" });
        var collection = new RequestCollection { Auth = auth };
        collection.Variables.Add(new() { Key = "client", InitialValue = "collection-client" });
        var tab = RequestTabViewModel.FromRequest(new() { Auth = inherited ? new() { Type = AuthType.Inherit } : auth },
            environmentProvider: () => environment, oauthTokenService: oauth);
        tab.WorkspaceProvider = () => workspace;
        tab.CollectionProvider = () => collection;
        await tab.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        Assert.NotNull(oauth.Prepared);
        Assert.Equal(new OAuthContext(environment.Id, OAuthGrantType.AuthorizationCodePkce, "https://auth.test/authorize",
            "https://auth.test/token", "interactive-client", "private &\"value", "read write", "http://127.0.0.1:12345/"), OAuthContext.From(oauth.Prepared));
        Assert.Equal("•••••••• •••••••• •••••••• ••••••••", tab.AuthEditor.AuthorizationMessage);
        Assert.Equal("{{client}}", auth.ClientId);
    }

    private static RequestAuth InteractiveAuth() => new() {
        Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce,
        ClientId = "{{client}}", ClientSecret = "{{secret}}", AuthorizationUrl = "{{authorize}}",
        TokenUrl = "{{token}}", Scope = "{{scope}}", RedirectUri = "{{redirect}}" };

    private sealed class PreparedOAuth : IOAuthTokenService
    {
        public RequestAuth? Prepared { get; private set; }
        public string? Failure { get; init; }
        public Task<OAuthToken> AuthorizeWithPkceAsync(RequestAuth auth, CancellationToken cancellationToken = default)
        {
            Prepared = auth.Clone();
            if (Failure is not null) throw new InvalidOperationException(Failure);
            return Task.FromResult(new OAuthToken("prepared", null, DateTimeOffset.UtcNow.AddHours(1)));
        }
        public Task<string> GetValidAccessTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> RequestClientCredentialsTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> RefreshTokenAsync(RequestAuth auth, string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearCachedTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
