using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

// Only cookie header values are classified as session secrets; response bodies are never scanned.
internal static class SessionSecrets
{
    public static IEnumerable<string> FromCookieHeader(string header) => header.Split(';')
        .Select(PairValue).Where(value => !string.IsNullOrEmpty(value));

    public static string FromSetCookieHeader(string header) => PairValue(header.Split(';')[0]);

    private static string PairValue(string pair)
    {
        var equals = pair.IndexOf('=');
        return equals < 0 ? string.Empty : pair[(equals + 1)..].Trim().Trim('"');
    }

    public static RequestItem RedactSnapshot(RequestItem source, IEnumerable<string> secrets)
    {
        var values = secrets.Concat(source.Headers
            .Where(header => header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => FromCookieHeader(header.Value))).ToArray();
        var redactor = new SecretRedactor();
        string Redact(string? text) => redactor.Redact(text ?? string.Empty, values);
        var snapshot = source.CloneForEditing();
        snapshot.Name = Redact(snapshot.Name);
        snapshot.Url = Redact(snapshot.Url);
        snapshot.Body = Redact(snapshot.Body);
        snapshot.BodyContentType = Redact(snapshot.BodyContentType);
        snapshot.BodyFilePath = source.BodyFilePath is null ? null : Redact(source.BodyFilePath);
        snapshot.ProxyUrl = Redact(snapshot.ProxyUrl);
        snapshot.Auth.Username = Redact(snapshot.Auth.Username);
        snapshot.Auth.ClientId = Redact(snapshot.Auth.ClientId);
        snapshot.Auth.ApiKeyHeaderName = Redact(snapshot.Auth.ApiKeyHeaderName);
        snapshot.Auth.ApiKeyQueryParam = Redact(snapshot.Auth.ApiKeyQueryParam);
        snapshot.Auth.AuthorizationUrl = Redact(snapshot.Auth.AuthorizationUrl);
        snapshot.Auth.TokenUrl = Redact(snapshot.Auth.TokenUrl);
        snapshot.Auth.Scope = Redact(snapshot.Auth.Scope);
        snapshot.Auth.RedirectUri = Redact(snapshot.Auth.RedirectUri);
        foreach (var assertion in snapshot.Assertions)
        {
            assertion.Target = Redact(assertion.Target);
            assertion.ExpectedValue = Redact(assertion.ExpectedValue);
            if (assertion.Description is not null) assertion.Description = Redact(assertion.Description);
        }
        snapshot.PreRequestScript = Redact(snapshot.PreRequestScript);
        snapshot.TestScript = Redact(snapshot.TestScript);
        foreach (var row in snapshot.Headers.Concat(snapshot.QueryParams).Concat(snapshot.FormData))
        {
            row.Key = Redact(row.Key);
            row.Value = Redact(row.Value);
            row.FilePath = Redact(row.FilePath);
            row.Description = Redact(row.Description);
        }
        return snapshot;
    }
}
