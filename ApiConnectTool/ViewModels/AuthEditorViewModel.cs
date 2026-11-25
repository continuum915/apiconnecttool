using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public sealed record AuthChoice(AuthType Type, string Label);

public partial class AuthEditorViewModel : ObservableObject, IDisposable
{
    private readonly IOAuthTokenService? _oauth;
    /// <summary>The environment pair the token actions bind to; matches what Send uses.</summary>
    public Func<OAuthEnvironmentIds>? EnvironmentIdsProvider { get; set; }
    public Func<VariableScopeMap>? VariableScopesProvider { get; set; }
    private readonly Func<EffectiveAuth>? _effective;
    private readonly List<INotifyPropertyChanged> _parents = new();
    private RequestCollection? _collection;
    [ObservableProperty] private RequestAuth _auth;
    [ObservableProperty] private AuthChoice? _selectedAuthChoice;
    [ObservableProperty] private bool _isPasswordVisible;
    [ObservableProperty] private bool _isAuthorizing;
    [ObservableProperty] private string? _authorizationMessage;
    public IReadOnlyList<AuthChoice> AuthChoices { get; }
    public int SelectedAuthTypeIndex => (int)Auth.Type;
    public int SelectedOAuthGrantTypeIndex { get => (int)Auth.GrantType; set => Auth.GrantType = (OAuthGrantType)value; }
    public char PasswordChar => IsPasswordVisible ? '\0' : '●';
    public bool IsInheriting => Auth.Type == AuthType.Inherit;
    public string InheritanceLabel
    {
        get
        {
            var effective = _effective?.Invoke();
            return effective is null || effective.Auth.Type == AuthType.None
                ? Text("Str.Auth.NoParent")
                : string.Format(Text("Str.Auth.Source"), Label(effective.Auth.Type), effective.SourceName);
        }
    }
    public AuthEditorViewModel(RequestAuth auth, IOAuthTokenService? oauth = null, Func<EffectiveAuth>? effective = null,
        bool allowInheritance = true)
    {
        _auth = auth; _oauth = oauth; _effective = effective;
        AuthChoices = (allowInheritance ? new[] { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey, AuthType.OAuth2 }
            : new[] { AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey, AuthType.OAuth2 })
            .Select(type => new AuthChoice(type, Label(type))).ToArray();
        Auth.PropertyChanged += AuthChanged;
        SelectedAuthChoice = AuthChoices.First(choice => choice.Type == auth.Type);
    }
    private static string Text(string key)
    {
        var text = AppLocalization.Get(key);
        return text == key ? DefaultStrings.Get(key) : text;
    }
    private static string Label(AuthType type) => Text($"Str.Auth.Type.{type}");
    partial void OnSelectedAuthChoiceChanged(AuthChoice? value) { if (value is not null) Auth.Type = value.Type; }
    partial void OnIsPasswordVisibleChanged(bool value) => OnPropertyChanged(nameof(PasswordChar));
    partial void OnAuthChanging(RequestAuth value) => Auth.PropertyChanged -= AuthChanged;
    partial void OnAuthChanged(RequestAuth value) { value.PropertyChanged += AuthChanged; AuthChanged(this, new(null)); }
    private void AuthChanged(object? sender, PropertyChangedEventArgs args)
    {
        SelectedAuthChoice = AuthChoices.First(choice => choice.Type == Auth.Type);
        OnPropertyChanged(nameof(SelectedAuthTypeIndex)); OnPropertyChanged(nameof(SelectedOAuthGrantTypeIndex));
        OnPropertyChanged(nameof(IsInheriting)); Refresh();
    }
    public void ObserveParents(RequestCollection? collection)
    {
        _collection = collection;
        foreach (var parent in _parents) parent.PropertyChanged -= ParentChanged;
        _parents.Clear();
        if (collection is not null)
        {
            _parents.Add(collection); _parents.Add(collection.Auth);
            foreach (var folder in collection.Folders) { _parents.Add(folder); _parents.Add(folder.Auth); }
        }
        foreach (var parent in _parents) parent.PropertyChanged += ParentChanged;
        Refresh();
    }
    private void ParentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RequestCollection.Auth)) ObserveParents(_collection);
        else Refresh();
    }
    public void Refresh() => OnPropertyChanged(nameof(InheritanceLabel));
    public void ConfigureScopes(Workspace? workspace, RequestCollection collection, Func<RequestEnvironment?>? environment)
    {
        EnvironmentIdsProvider = () => new(environment?.Invoke()?.Id, collection.ActiveEnvironment?.Id);
        VariableScopesProvider = () => VariableScopeBuilder.Build(workspace, collection, environment?.Invoke(),
            collectionEnvironment: collection.ActiveEnvironment);
    }
    [RelayCommand] private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;
    [RelayCommand] private Task AuthorizeAsync() => RunOAuthAsync(async (oauth, auth) =>
    {
        if (auth.GrantType == OAuthGrantType.ClientCredentials) await oauth.GetValidAccessTokenAsync(auth);
        else await oauth.AuthorizeWithPkceAsync(auth);
        return "Str.Auth.Authorized";
    });
    [RelayCommand] private Task ClearTokenAsync() => RunOAuthAsync(async (oauth, auth) =>
    {
        await oauth.ClearCachedTokenAsync(auth);
        return "Str.Auth.TokenCleared";
    });
    [RelayCommand] private Task GetNewTokenAsync() => RunOAuthAsync(async (oauth, auth) =>
    {
        if (auth.GrantType != OAuthGrantType.ClientCredentials) return "Str.Auth.NewTokenClientCredentialsOnly";
        await oauth.ClearCachedTokenAsync(auth);
        await oauth.GetValidAccessTokenAsync(auth);
        return "Str.Auth.NewTokenObtained";
    });
    // Every token action resolves the effective (possibly inherited) auth against the active
    // variable scopes and environment, so all of them address the same cache context as Send.
    private async Task RunOAuthAsync(Func<IOAuthTokenService, RequestAuth, Task<string>> action)
    {
        if (_oauth is null || IsAuthorizing) return;
        IsAuthorizing = true;
        var secrets = new List<string>();
        try
        {
            var auth = (IsInheriting ? _effective?.Invoke().Auth ?? Auth : Auth).Clone();
            EnvironmentIdsProvider?.Invoke().ApplyTo(auth);
            var scopes = VariableScopesProvider?.Invoke() ?? VariableScopeBuilder.Build();
            secrets = RequestSecrets.Collect(new() { Auth = auth }, scopes.Variables, scopes.SecretNames);
            var resolver = new VariableResolver();
            auth = RequestVariableTemplates.Transform(new() { Auth = auth },
                value => resolver.Resolve(value, scopes.Variables).Value).Auth;
            OAuthAuthValidation.ValidateTemplates(auth, Text);
            AuthorizationMessage = Text(await action(_oauth, auth));
        }
        catch (Exception exception) { AuthorizationMessage = new SecretRedactor().Redact(exception.Message, secrets); }
        finally { IsAuthorizing = false; }
    }
    public void Dispose()
    {
        Auth.PropertyChanged -= AuthChanged;
        foreach (var parent in _parents) parent.PropertyChanged -= ParentChanged;
        _parents.Clear();
    }
}
