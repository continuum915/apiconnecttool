using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;

namespace ApiConnectTool.Services;

internal static class PostmanAuthMapper
{
    public static RequestAuth Import(PostmanAuthDto? dto, string name, PostmanImportReport report, bool collection = false)
    {
        var auth = new RequestAuth { Type = dto is null && !collection ? AuthType.Inherit : AuthType.None };
        string Value(List<PostmanAuthParamDto>? rows, string key) => rows?.FirstOrDefault(row => row.Key == key)?.Value ?? string.Empty;
        switch (dto?.Type)
        {
            case null: case "noauth": break;
            case "bearer": auth.Type = AuthType.Bearer; auth.Token = Value(dto.Bearer, "token"); break;
            case "basic": auth.Type = AuthType.Basic; auth.Username = Value(dto.Basic, "username"); auth.Password = Value(dto.Basic, "password"); break;
            case "apikey":
                auth.Type = AuthType.ApiKey; auth.Token = Value(dto.Apikey, "value");
                if (Value(dto.Apikey, "in") == "query") auth.ApiKeyQueryParam = Value(dto.Apikey, "key");
                else auth.ApiKeyHeaderName = Value(dto.Apikey, "key");
                break;
            case "oauth2" when dto.Oauth2 is not null && Value(dto.Oauth2, "grant_type") is "client_credentials" or "authorization_code":
                auth.Type = AuthType.OAuth2; auth.ClientId = Value(dto.Oauth2, "clientId"); auth.ClientSecret = Value(dto.Oauth2, "clientSecret");
                auth.TokenUrl = Value(dto.Oauth2, "accessTokenUrl"); auth.AuthorizationUrl = Value(dto.Oauth2, "authUrl");
                auth.Scope = Value(dto.Oauth2, "scope"); auth.RedirectUri = Value(dto.Oauth2, "redirect_uri");
                auth.GrantType = Value(dto.Oauth2, "grant_type") == "client_credentials" ? OAuthGrantType.ClientCredentials : OAuthGrantType.AuthorizationCodePkce;
                if (dto.Oauth2?.Any(row => row.Key == "accessToken" || row.Key == "refreshToken") == true)
                    report.AddWarning(string.Format(Localization.DefaultStrings.Get("Str.Postman.DroppedTokens"), name));
                break;
            default: report.AddWarning(string.Format(Localization.DefaultStrings.Get("Str.Postman.UnsupportedAuth"), dto.Type, name)); break;
        }
        return auth;
    }
    public static PostmanAuthDto? Export(RequestAuth auth, bool includeSecrets)
    {
        PostmanAuthParamDto Pair(string key, string value) => new() { Key = key, Value = value };
        string Secret(string value) => includeSecrets ? value : string.Empty;
        return auth.Type switch
        {
            AuthType.Inherit => null,
            AuthType.None => new() { Type = "noauth" },
            AuthType.Bearer => new() { Type = "bearer", Bearer = [Pair("token", Secret(auth.Token.Value))] },
            AuthType.Basic => new() { Type = "basic", Basic = [Pair("username", auth.Username), Pair("password", Secret(auth.Password.Value))] },
            AuthType.ApiKey => new() { Type = "apikey", Apikey = [Pair("key", string.IsNullOrEmpty(auth.ApiKeyHeaderName) ? auth.ApiKeyQueryParam : auth.ApiKeyHeaderName),
                Pair("value", Secret(auth.Token.Value)), Pair("in", string.IsNullOrEmpty(auth.ApiKeyHeaderName) ? "query" : "header")] },
            AuthType.OAuth2 => new() { Type = "oauth2", Oauth2 = [Pair("clientId", auth.ClientId), Pair("clientSecret", Secret(auth.ClientSecret.Value)),
                Pair("accessTokenUrl", auth.TokenUrl), Pair("authUrl", auth.AuthorizationUrl), Pair("scope", auth.Scope), Pair("redirect_uri", auth.RedirectUri),
                Pair("grant_type", auth.GrantType == OAuthGrantType.ClientCredentials ? "client_credentials" : "authorization_code")] },
            _ => throw new ArgumentOutOfRangeException(nameof(auth))
        };
    }
}
