using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed class CurlGenerator(IVariableResolver? variableResolver = null, ISecretRedactor? secretRedactor = null) : ICurlGenerator
{
    private static readonly IReadOnlyDictionary<string, string> EmptyVariables = new Dictionary<string, string>();

    private readonly IVariableResolver _variableResolver = variableResolver ?? new VariableResolver();
    private readonly ISecretRedactor _secretRedactor = secretRedactor ?? new SecretRedactor();

    public string Generate(RequestItem request, IReadOnlyDictionary<string, string> variables, bool revealSecrets)
    {
        ArgumentNullException.ThrowIfNull(request);
        variables ??= EmptyVariables;

        var resolved = Resolve(request, variables, out var secretValues);
        var command = Build(resolved);

        return revealSecrets ? command : _secretRedactor.Redact(command, secretValues);
    }

    /// <summary>
    /// Substitutes {{name}} tokens the same way RequestExecutionService does for an actual send,
    /// so the generated command matches what would really go out - and collects the same class of
    /// auth-derived secret literals (token/password/client secret) RequestExecutionService tracks,
    /// so redaction doesn't need a live send to know what to hide.
    /// </summary>
    private RequestItem Resolve(RequestItem definition, IReadOnlyDictionary<string, string> variables, out List<string> secretValues)
    {
        string ResolveText(string? value) => _variableResolver.Resolve(value, variables).Value;
        KeyValueItem ResolveRow(KeyValueItem row) => new()
        {
            UseRawQueryText = row.UseRawQueryText,
            HasQueryEquals = row.HasQueryEquals,
            FieldType = row.FieldType,
            FilePath = ResolveText(row.FilePath),
            IsEnabled = row.IsEnabled,
            Key = ResolveText(row.Key),
            Value = ResolveText(row.Value),
            Description = row.Description
        };

        var resolved = new RequestItem
        {
            Method = definition.Method,
            Url = ResolveText(definition.Url),
            QueryParams = new(definition.QueryParams.Select(ResolveRow)),
            Headers = new(definition.Headers.Select(ResolveRow)),
            BodyMode = definition.BodyMode,
            BodyContentType = ResolveText(definition.BodyContentType),
            Body = ResolveText(definition.Body),
            FormData = new(definition.FormData.Select(ResolveRow)),
            BodyFilePath = definition.BodyFilePath,
            Auth = new RequestAuth
            {
                Type = definition.Auth.Type,
                Token = new EncryptedString(ResolveText(definition.Auth.Token.Value)),
                Username = ResolveText(definition.Auth.Username),
                Password = new EncryptedString(ResolveText(definition.Auth.Password.Value)),
                ApiKeyHeaderName = ResolveText(definition.Auth.ApiKeyHeaderName),
                ApiKeyQueryParam = ResolveText(definition.Auth.ApiKeyQueryParam),
                ClientSecret = new EncryptedString(ResolveText(definition.Auth.ClientSecret.Value))
            }
        };

        secretValues = RequestSecrets.Collect(resolved, EmptyVariables, new HashSet<string>());

        return resolved;
    }

    private static string Build(RequestItem request)
    {
        var lines = new List<string> { $"curl -X {request.Method.Method} {ShellQuote(BuildUrl(request))}" };

        foreach (var header in request.Headers.Where(IsEnabledWithKey))
        {
            lines.Add($"-H {ShellQuote($"{header.Key}: {header.Value}")}");
        }

        AppendAuth(request.Auth, lines);
        AppendBody(request, lines);

        return string.Join(" \\\n  ", lines);
    }

    private static string BuildUrl(RequestItem request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            // Still an unresolved {{token}} or otherwise invalid - emit it as-is rather than
            // failing generation outright; the command just won't be runnable until fixed.
            return request.Url;
        }

        IEnumerable<KeyValueItem> parameters = request.QueryParams;

        // API-key-as-query-param isn't a header, so it only ever shows up here.
        if (request.Auth.Type == AuthType.ApiKey &&
            string.IsNullOrEmpty(request.Auth.ApiKeyHeaderName) &&
            !string.IsNullOrEmpty(request.Auth.ApiKeyQueryParam) &&
            !string.IsNullOrEmpty(request.Auth.Token.Value))
        {
            parameters = parameters.Append(new KeyValueItem { Key = request.Auth.ApiKeyQueryParam, Value = request.Auth.Token.Value });
        }

        return RequestQueryString.AppendToUri(uri, parameters).AbsoluteUri;
    }

    private static void AppendAuth(RequestAuth auth, List<string> lines)
    {
        switch (auth.Type)
        {
            case AuthType.Bearer:
                if (!string.IsNullOrEmpty(auth.Token.Value))
                {
                    lines.Add($"-H {ShellQuote($"Authorization: Bearer {auth.Token.Value}")}");
                }
                break;
            case AuthType.Basic:
                if (!string.IsNullOrWhiteSpace(auth.Username))
                {
                    lines.Add($"-u {ShellQuote($"{auth.Username}:{auth.Password.Value}")}");
                }
                break;
            case AuthType.ApiKey:
                if (!string.IsNullOrEmpty(auth.ApiKeyHeaderName) && !string.IsNullOrEmpty(auth.Token.Value))
                {
                    lines.Add($"-H {ShellQuote($"{auth.ApiKeyHeaderName}: {auth.Token.Value}")}");
                }
                // Query-param case is folded into the URL by BuildUrl instead.
                break;
            case AuthType.OAuth2:
                // Reproducing this needs a live/cached access token, which "Copy as cURL" doesn't
                // fetch as a side effect (that would be a surprising network call/token refresh
                // triggered by a clipboard action). Omitting the header is an honest "not
                // reproduced" rather than silently emitting an unauthenticated command that looks
                // complete.
                break;
        }
    }

    private static void AppendBody(RequestItem request, List<string> lines)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.BodyMode == RequestBodyMode.None)
        {
            return;
        }

        switch (request.BodyMode)
        {
            case RequestBodyMode.Raw:
                lines.Add($"--data-raw {ShellQuote(request.Body ?? string.Empty)}");
                break;
            case RequestBodyMode.UrlEncoded:
                var encoded = string.Join("&", request.FormData.Where(row => IsEnabledWithKey(row) && !row.IsFile)
                    .Select(row => $"{Uri.EscapeDataString(row.Key)}={Uri.EscapeDataString(row.Value)}"));
                lines.Add($"--data {ShellQuote(encoded)}");
                break;
            case RequestBodyMode.FormData:
                foreach (var row in request.FormData.Where(IsEnabledWithKey))
                {
                    lines.Add($"-F {ShellQuote($"{row.Key}={(row.IsFile ? "@" + row.FilePath : row.Value)}")}");
                }
                break;
            case RequestBodyMode.Binary:
                lines.Add(string.IsNullOrWhiteSpace(request.BodyFilePath)
                    ? "--data-binary '<no file selected>'"
                    : $"--data-binary {ShellQuote("@" + request.BodyFilePath)}");
                break;
        }
    }

    private static bool IsEnabledWithKey(KeyValueItem row) => row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key);

    private static string ShellQuote(string value) => "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
}
