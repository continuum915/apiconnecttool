using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>
/// Reads OAuth token endpoint responses tolerantly and turns failures into localized,
/// redacted messages. Provider error text is shown only from JSON <c>error</c> and
/// <c>error_description</c> fields, never from arbitrary bodies.
/// </summary>
internal static class OAuthTokenResponse
{
    private const long DefaultLifetimeSeconds = 3600;
    private const long MaxLifetimeSeconds = 10L * 365 * 24 * 3600;
    private const int MaxProviderErrorLength = 300;
    private static readonly string[] SecretFormFields = ["client_secret", "refresh_token", "code", "code_verifier"];

    public static OAuthToken Parse(string responseBody, string? fallbackRefreshToken, OAuthContext context)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var accessTokenElement)
            || accessTokenElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(accessTokenElement.GetString()))
        {
            throw new FormatException("The token response has no string access_token.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
                           && refreshTokenElement.ValueKind == JsonValueKind.String
                           && !string.IsNullOrEmpty(refreshTokenElement.GetString())
            ? refreshTokenElement.GetString()
            : fallbackRefreshToken;

        return new OAuthToken(accessTokenElement.GetString()!, refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(ReadExpiresIn(root))) { Context = context };
    }

    private static long ReadExpiresIn(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var element))
        {
            return DefaultLifetimeSeconds;
        }

        var seconds = 0L;
        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out seconds),
            JsonValueKind.String => long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds),
            _ => false
        };
        return parsed && seconds >= 0 && seconds <= MaxLifetimeSeconds ? seconds : DefaultLifetimeSeconds;
    }

    public static string FormatFailure(int statusCode, string responseBody, RequestAuth auth, Dictionary<string, string> form)
    {
        var detail = ReadProviderError(responseBody);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get("Str.OAuth.RequestFailed"), statusCode);
        }

        var secrets = new List<string> { auth.ClientSecret.Value };
        foreach (var key in SecretFormFields)
        {
            if (form.TryGetValue(key, out var value)) secrets.Add(value);
        }

        detail = new SecretRedactor().Redact(detail, secrets);
        if (detail.Length > MaxProviderErrorLength)
        {
            detail = detail[..MaxProviderErrorLength];
        }

        return string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get("Str.OAuth.RequestFailedWithDetail"), statusCode, detail);
    }

    private static string? ReadProviderError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var error = ReadString(root, "error");
            var description = ReadString(root, "error_description");
            return (error, description) switch
            {
                (null, null) => null,
                (not null, null) => error,
                (null, not null) => description,
                _ => error + ": " + description
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;
}
