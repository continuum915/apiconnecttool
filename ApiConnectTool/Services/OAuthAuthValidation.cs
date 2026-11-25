using System;
using System.Collections.Generic;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

internal static class OAuthAuthValidation
{
    public static void ValidateTemplates(RequestAuth auth, Func<string, string>? text = null)
    {
        text ??= DefaultStrings.Get;
        var fields = new List<(string Name, string Value)> {
            ("ClientId", auth.ClientId), ("ClientSecret", auth.ClientSecret.Value),
            ("TokenUrl", auth.TokenUrl), ("Scope", auth.Scope) };
        if (auth.GrantType == OAuthGrantType.AuthorizationCodePkce)
        {
            fields.Add(("AuthorizationUrl", auth.AuthorizationUrl));
            fields.Add(("RedirectUri", auth.RedirectUri));
        }
        var resolver = new VariableResolver();
        foreach (var (name, value) in fields)
            if (resolver.Resolve(value, new Dictionary<string, string>()).UnresolvedVariableNames.Count > 0)
                throw new InvalidOperationException(string.Format(text("Str.OAuth.UnresolvedField"), text("Str.Auth.Editor." + name)));
    }
}
