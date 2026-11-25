using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

internal static class RequestSecrets
{
    public static List<string> Collect(RequestItem request, IReadOnlyDictionary<string, string> variables,
        IReadOnlySet<string> names)
    {
        var resolver = new VariableResolver();
        var values = names.Where(variables.ContainsKey).Select(name => variables[name]).ToList();
        foreach (var value in new[] { request.Auth.Token.Value, request.Auth.Password.Value, request.Auth.ClientSecret.Value })
            if (!string.IsNullOrEmpty(value)) values.Add(resolver.Resolve(value, variables).Value);
        if (request.Auth.Type == AuthType.Basic && !string.IsNullOrWhiteSpace(request.Auth.Username))
            values.Add(BasicCredential(resolver.Resolve(request.Auth.Username, variables).Value,
                resolver.Resolve(request.Auth.Password.Value, variables).Value));
        return values;
    }

    public static string BasicCredential(string username, string password) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    // Known values only: no scanning or guessing credentials from response contents.
    public static IEnumerable<string> Forms(string value)
    {
        yield return value;
        yield return Uri.EscapeDataString(value);
        yield return System.Net.WebUtility.UrlEncode(value);
        yield return JsonSerializer.Serialize(value)[1..^1];
        yield return JsonSerializer.Serialize(value, RelaxedJson)[1..^1];
    }

    private static readonly JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
