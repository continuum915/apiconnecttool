using System;
using System.Collections.ObjectModel;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

// One traversal for execution and inspection; only fields supported by resolution belong here.
public static class RequestVariableTemplates
{
    public static RequestItem Transform(RequestItem definition, Func<string?, string> resolve)
    {
        KeyValueItem ResolveRow(KeyValueItem row) => new()
        {
            UseRawQueryText = row.UseRawQueryText,
            HasQueryEquals = row.HasQueryEquals,
            FieldType = row.FieldType,
            FilePath = resolve(row.FilePath),
            IsEnabled = row.IsEnabled,
            Key = resolve(row.Key),
            Value = resolve(row.Value),
            Description = row.Description
        };

        return new RequestItem
        {
            Id = definition.Id,
            Name = definition.Name,
            Method = definition.Method,
            Url = resolve(definition.Url),
            QueryParams = new ObservableCollection<KeyValueItem>(definition.QueryParams.Select(ResolveRow)),
            Headers = new ObservableCollection<KeyValueItem>(definition.Headers.Select(ResolveRow)),
            BodyMode = definition.BodyMode,
            BodyContentType = resolve(definition.BodyContentType),
            Body = resolve(definition.Body),
            FormData = new ObservableCollection<KeyValueItem>(definition.FormData.Select(ResolveRow)),
            BodyFilePath = definition.BodyFilePath,
            Auth = new RequestAuth
            {
                OAuthEnvironmentId = definition.Auth.OAuthEnvironmentId,
                OAuthCollectionEnvironmentId = definition.Auth.OAuthCollectionEnvironmentId,
                Type = definition.Auth.Type,
                Token = new EncryptedString(resolve(definition.Auth.Token.Value)),
                Username = resolve(definition.Auth.Username),
                Password = new EncryptedString(resolve(definition.Auth.Password.Value)),
                ApiKeyHeaderName = resolve(definition.Auth.ApiKeyHeaderName),
                ApiKeyQueryParam = resolve(definition.Auth.ApiKeyQueryParam),
                ClientId = resolve(definition.Auth.ClientId),
                ClientSecret = new EncryptedString(resolve(definition.Auth.ClientSecret.Value)),
                AuthorizationUrl = resolve(definition.Auth.AuthorizationUrl),
                TokenUrl = resolve(definition.Auth.TokenUrl),
                Scope = resolve(definition.Auth.Scope),
                RedirectUri = resolve(definition.Auth.RedirectUri),
                GrantType = definition.Auth.GrantType,
                OAuthCacheKey = definition.Auth.OAuthCacheKey
            }
        };
    }
}
