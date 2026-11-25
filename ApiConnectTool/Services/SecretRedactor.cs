using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiConnectTool.Services;

public sealed class SecretRedactor : ISecretRedactor
{
    /// <summary>What a redacted secret is replaced with, everywhere secrets are hidden.</summary>
    public const string Placeholder = "••••••••";

    public string Redact(string text, IEnumerable<string> knownSecretValues)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Longest-first: a secret that is a substring of another must be redacted after its
        // superstring, so the superstring's occurrence is fully replaced before a shorter value
        // could partially match what's left of it and leak a fragment.
        var secrets = knownSecretValues
            .Where(value => !string.IsNullOrEmpty(value))
            .SelectMany(RequestSecrets.Forms)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length, Comparer<int>.Default);

        foreach (var secret in secrets)
        {
            text = text.Replace(secret, Placeholder, StringComparison.Ordinal);
        }

        return text;
    }
}
