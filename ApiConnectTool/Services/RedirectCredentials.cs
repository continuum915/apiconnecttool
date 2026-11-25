using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

internal static class RedirectCredentials
{
    public static bool SameOrigin(Uri source, Uri destination) =>
        source.Scheme == destination.Scheme && source.IdnHost.Equals(destination.IdnHost, StringComparison.OrdinalIgnoreCase)
        && source.Port == destination.Port;

    public static void ValidateDestination(Uri source, Uri destination)
    {
        if (source.Scheme == Uri.UriSchemeHttps && destination.Scheme == Uri.UriSchemeHttp)
            throw new InvalidOperationException(DefaultStrings.Get("Str.Redirect.HttpsDowngrade"));
        if (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(DefaultStrings.Get("Str.Redirect.UnsupportedScheme"));
    }

    public static void Apply(HttpRequestMessage message, RequestAuth auth, IReadOnlySet<string> secrets, bool retainCredentials)
    {
        if (retainCredentials) return;
        var forms = secrets.Where(value => value.Length > 0).SelectMany(RequestSecrets.Forms).Distinct().ToArray();
        bool ContainsSecret(IEnumerable<string> values) =>
            values.Any(value => forms.Any(secret => value.Contains(secret, StringComparison.Ordinal)));
        foreach (var header in message.Headers.ToArray())
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || (auth.Type == AuthType.ApiKey && header.Key.Equals(auth.ApiKeyHeaderName, StringComparison.OrdinalIgnoreCase))
                || ContainsSecret(header.Value))
                message.Headers.Remove(header.Key);
        if (message.Content is null) return;
        foreach (var header in message.Content.Headers.ToArray())
            if (ContainsSecret(header.Value))
                message.Content.Headers.Remove(header.Key);
    }
}
