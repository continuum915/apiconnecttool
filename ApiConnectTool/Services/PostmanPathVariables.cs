using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ApiConnectTool.Serialization;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>
/// ApiConnectTool has no path-variable model, so Postman <c>:name</c> path segments declared in the URL's
/// variable array are inlined on import: a non-empty value (including a <c>{{var}}</c>
/// reference) replaces the segment, and a missing or empty value becomes a <c>{{name}}</c>
/// placeholder so the unresolved-variable banner shows what to define. Undeclared segments and
/// colons that are not a whole path segment (schemes, ports) are left unchanged.
/// </summary>
internal static class PostmanPathVariables
{
    public static string Inline(string url, IReadOnlyList<PostmanPathVariableDto>? declared, string requestName, PostmanImportReport report)
    {
        if (string.IsNullOrEmpty(url) || declared is null || declared.Count == 0)
        {
            return url;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in declared)
        {
            if (!string.IsNullOrEmpty(variable.Key)) values.TryAdd(variable.Key, ReadValue(variable.Value));
        }

        var pathEnd = url.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? url : url[..pathEnd];
        var segments = path.Split('/');
        var inlined = new List<string>();
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length < 2 || segment[0] != ':' || !values.TryGetValue(segment[1..], out var value))
            {
                continue;
            }

            var name = segment[1..];
            if (string.IsNullOrEmpty(value))
            {
                value = "{{" + name + "}}";
                report.AddWarning(Format("Str.Postman.PathVariablePlaceholder", name, requestName, value));
            }

            segments[i] = value;
            inlined.Add(name);
        }

        if (inlined.Count == 0)
        {
            return url;
        }

        report.AddNote(Format("Str.Postman.PathVariablesInlined", string.Join(", ", inlined.Distinct(StringComparer.Ordinal)), requestName));
        return string.Join('/', segments) + (pathEnd < 0 ? string.Empty : url[pathEnd..]);
    }

    private static string ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => string.Empty
    };

    private static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get(key), args);
}
