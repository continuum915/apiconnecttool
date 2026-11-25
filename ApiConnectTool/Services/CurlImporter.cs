using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>
/// Parses a pasted `curl ...` command into a <see cref="RequestItem"/>. Deliberately supports only
/// these flags (-X/--request, -H/--header, -d/--data/--data-raw/
/// --data-binary, -u/--user, -b/--cookie, and the bare URL) plus a short allowlist of common,
/// request-irrelevant curl flags (redirect-follow, verbosity, TLS bypass) that are recognized and
/// ignored rather than rejected - anything else fails the import outright instead of silently
/// dropping information.
/// </summary>
public sealed class CurlImporter : ICurlImporter
{
    private static readonly HashSet<string> IgnoredFlags = new(StringComparer.Ordinal)
    {
        "-L", "--location", "-s", "--silent", "-k", "--insecure", "-i", "--include", "-v", "--verbose"
    };

    public RequestItem Import(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var tokens = Tokenize(command);
        if (tokens.Count == 0 || !string.Equals(tokens[0], "curl", StringComparison.OrdinalIgnoreCase))
        {
            throw new CurlImportException("Expected a command starting with 'curl'.");
        }

        var request = new RequestItem { BodyMode = RequestBodyMode.None, Name = "Imported request" };
        string? url = null;
        string? explicitMethod = null;
        var hasBody = false;

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token)
            {
                case "-X":
                case "--request":
                    explicitMethod = RequireValue(tokens, ref i, token);
                    break;
                case "-H":
                case "--header":
                    ApplyHeader(request, RequireValue(tokens, ref i, token));
                    break;
                case "-d":
                case "--data":
                    ApplyData(request, RequireValue(tokens, ref i, token));
                    hasBody = true;
                    break;
                case "--data-raw":
                    request.BodyMode = RequestBodyMode.Raw;
                    request.Body = RequireValue(tokens, ref i, token);
                    hasBody = true;
                    break;
                case "--data-binary":
                    ApplyDataBinary(request, RequireValue(tokens, ref i, token));
                    hasBody = true;
                    break;
                case "-u":
                case "--user":
                    ApplyBasicAuth(request, RequireValue(tokens, ref i, token));
                    break;
                case "-b":
                case "--cookie":
                    request.Headers.Add(new KeyValueItem { IsEnabled = true, Key = "Cookie", Value = RequireValue(tokens, ref i, token) });
                    break;
                default:
                    if (IgnoredFlags.Contains(token))
                    {
                        break;
                    }
                    if (token.StartsWith('-'))
                    {
                        throw new CurlImportException($"Unsupported cURL flag: {token}");
                    }
                    if (url is not null)
                    {
                        throw new CurlImportException("Multiple URLs found in the cURL command.");
                    }
                    url = token;
                    break;
            }
        }

        if (string.IsNullOrEmpty(url))
        {
            throw new CurlImportException("No URL found in the cURL command.");
        }

        request.Url = url;
        request.Method = new HttpMethod((explicitMethod ?? (hasBody ? "POST" : "GET")).ToUpperInvariant());
        return request;
    }

    private static string RequireValue(IReadOnlyList<string> tokens, ref int i, string flag)
    {
        if (i + 1 >= tokens.Count)
        {
            throw new CurlImportException($"'{flag}' requires a value.");
        }

        return tokens[++i];
    }

    /// <summary>
    /// "Authorization: Bearer &lt;token&gt;" is recognized and folded into Auth (Bearer) instead
    /// of staying a plain header - the same shape CurlGenerator (4f) itself emits for Bearer auth,
    /// so generate-then-import round-trips cleanly. Everything else stays a plain header; this
    /// importer doesn't attempt to guess API-key auth from an arbitrary custom header name.
    /// </summary>
    private static void ApplyHeader(RequestItem request, string headerText)
    {
        var separatorIndex = headerText.IndexOf(':');
        if (separatorIndex < 0)
        {
            throw new CurlImportException($"Malformed header: '{headerText}'.");
        }

        var key = headerText[..separatorIndex].Trim();
        var value = headerText[(separatorIndex + 1)..].Trim();

        if (string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase) &&
            value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Auth.Type = AuthType.Bearer;
            request.Auth.Token = value["Bearer ".Length..].Trim();
            return;
        }

        request.Headers.Add(new KeyValueItem { IsEnabled = true, Key = key, Value = value });
    }

    /// <summary>
    /// curl's -d/--data has no dedicated "this is form-encoded" marker; a value that looks like
    /// key=value[&key=value...] pairs (and doesn't start with a JSON brace) is treated as
    /// UrlEncoded form data, everything else (including JSON passed via -d rather than
    /// --data-raw, which real hand-typed/browser-copied commands frequently do) as a raw body.
    /// This matches exactly what CurlGenerator (4f) itself ever emits for -d - it only ever uses
    /// --data-raw for JSON/raw bodies - so generate-then-import round-trips correctly.
    /// </summary>
    private static void ApplyData(RequestItem request, string data)
    {
        var looksLikeFormPairs = data.Length > 0 && data[0] != '{' && data[0] != '[' && data.Contains('=');
        if (!looksLikeFormPairs)
        {
            request.BodyMode = RequestBodyMode.Raw;
            request.Body = data;
            return;
        }

        request.BodyMode = RequestBodyMode.UrlEncoded;
        foreach (var pair in data.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            request.FormData.Add(new KeyValueItem { IsEnabled = true, Key = key, Value = value });
        }
    }

    private static void ApplyDataBinary(RequestItem request, string value)
    {
        if (value.StartsWith('@'))
        {
            request.BodyMode = RequestBodyMode.Binary;
            request.BodyFilePath = value[1..];
        }
        else
        {
            request.BodyMode = RequestBodyMode.Raw;
            request.Body = value;
        }
    }

    private static void ApplyBasicAuth(RequestItem request, string userPass)
    {
        var separatorIndex = userPass.IndexOf(':');
        request.Auth.Type = AuthType.Basic;
        request.Auth.Username = separatorIndex < 0 ? userPass : userPass[..separatorIndex];
        request.Auth.Password = separatorIndex < 0 ? string.Empty : userPass[(separatorIndex + 1)..];
    }

    /// <summary>
    /// Minimal POSIX-ish shell-word tokenizer: single quotes are fully literal (no escapes
    /// recognized inside - matching CurlGenerator's own ShellQuote), double quotes recognize a
    /// backslash before ", \, $, or ` as an escape, and an unquoted backslash escapes the next
    /// character. An unterminated quote or trailing backslash is a syntax error, not a best-effort
    /// guess - matching this parser's "fail visibly on malformed input" contract.
    /// </summary>
    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var hasCurrent = false;
        var i = 0;

        void EndToken()
        {
            if (hasCurrent)
            {
                tokens.Add(current.ToString());
                current.Clear();
                hasCurrent = false;
            }
        }

        while (i < command.Length)
        {
            var c = command[i];
            if (char.IsWhiteSpace(c))
            {
                EndToken();
                i++;
                continue;
            }

            if (c == '\'')
            {
                hasCurrent = true;
                var closeIndex = command.IndexOf('\'', i + 1);
                if (closeIndex < 0)
                {
                    throw new CurlImportException("Unterminated single-quoted string.");
                }

                current.Append(command, i + 1, closeIndex - i - 1);
                i = closeIndex + 1;
            }
            else if (c == '"')
            {
                hasCurrent = true;
                i++;
                while (true)
                {
                    if (i >= command.Length)
                    {
                        throw new CurlImportException("Unterminated double-quoted string.");
                    }

                    var dc = command[i];
                    if (dc == '"')
                    {
                        i++;
                        break;
                    }

                    if (dc == '\\' && i + 1 < command.Length && "\"\\$`".IndexOf(command[i + 1]) >= 0)
                    {
                        current.Append(command[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        current.Append(dc);
                        i++;
                    }
                }
            }
            else if (c == '\\')
            {
                if (i + 1 >= command.Length)
                {
                    throw new CurlImportException("Trailing unescaped backslash.");
                }

                // A backslash immediately followed by a newline is a shell line continuation -
                // both characters are dropped and the token stream continues seamlessly, the same
                // way CurlGenerator's own " \\\n  " multi-line join must round-trip back in.
                if (command[i + 1] == '\n')
                {
                    i += 2;
                    continue;
                }
                if (command[i + 1] == '\r' && i + 2 < command.Length && command[i + 2] == '\n')
                {
                    i += 3;
                    continue;
                }

                hasCurrent = true;
                current.Append(command[i + 1]);
                i += 2;
            }
            else
            {
                hasCurrent = true;
                current.Append(c);
                i++;
            }
        }

        EndToken();
        return tokens;
    }
}
