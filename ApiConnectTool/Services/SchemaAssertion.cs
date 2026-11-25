using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

internal static class SchemaAssertion
{
    internal static (bool Passed, string Actual, string? Failure) Evaluate(string schemaText, string body)
    {
        JsonDocument schema;
        var options = new JsonDocumentOptions { MaxDepth = int.MaxValue };
        try { schema = JsonDocument.Parse(schemaText, options); }
        catch (JsonException) { return (false, "", Message("InvalidSchema", "$")); }
        using (schema)
        {
            var pending = new Stack<(JsonElement Schema, string Path)>();
            pending.Push((schema.RootElement, "$"));
            while (pending.TryPop(out var entry))
            {
                var node = entry.Schema;
                if (node.ValueKind != JsonValueKind.Object)
                    return (false, "", Message("InvalidSchema", entry.Path));
                foreach (var keyword in node.EnumerateObject())
                {
                    switch (keyword.Name)
                    {
                        case "type":
                            if (keyword.Value.ValueKind != JsonValueKind.String || !IsType(keyword.Value.GetString()))
                                return (false, "", Message("InvalidSchema", entry.Path + ".type"));
                            break;
                        case "required":
                            if (keyword.Value.ValueKind != JsonValueKind.Array)
                                return (false, "", Message("InvalidSchema", entry.Path + ".required"));
                            foreach (var name in keyword.Value.EnumerateArray())
                                if (name.ValueKind != JsonValueKind.String)
                                    return (false, "", Message("InvalidSchema", entry.Path + ".required"));
                            break;
                        case "enum":
                            if (keyword.Value.ValueKind != JsonValueKind.Array || keyword.Value.GetArrayLength() == 0)
                                return (false, "", Message("InvalidSchema", entry.Path + ".enum"));
                            break;
                        case "properties":
                            if (keyword.Value.ValueKind != JsonValueKind.Object)
                                return (false, "", Message("InvalidSchema", entry.Path + ".properties"));
                            foreach (var property in keyword.Value.EnumerateObject())
                                pending.Push((property.Value, PropertyPath(entry.Path + ".properties", property.Name)));
                            break;
                        case "items":
                            pending.Push((keyword.Value, entry.Path + ".items"));
                            break;
                    }
                }
            }

            JsonDocument response;
            try { response = JsonDocument.Parse(body, options); }
            catch (JsonException) { return (false, "", DefaultStrings.Get("Str.Assertions.InvalidJson")); }
            using (response)
            {
                var checks = new Stack<(JsonElement Schema, JsonElement Value, string Path)>();
                checks.Push((schema.RootElement, response.RootElement, "$"));
                while (checks.TryPop(out var check))
                {
                    var (node, value, path) = check;
                    if (node.TryGetProperty("type", out var type) && !MatchesType(value, type.GetString()!))
                        return (false, body, Message("SchemaTypeMismatch", path, type.GetString()!));
                    if (node.TryGetProperty("enum", out var allowed))
                    {
                        var found = false;
                        foreach (var candidate in allowed.EnumerateArray())
                            if (JsonElement.DeepEquals(candidate, value)) { found = true; break; }
                        if (!found) return (false, body, Message("SchemaEnumMismatch", path));
                    }
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        if (node.TryGetProperty("required", out var required))
                            foreach (var name in required.EnumerateArray())
                                if (!value.TryGetProperty(name.GetString()!, out _))
                                    return (false, body, Message("SchemaRequiredMissing", PropertyPath(path, name.GetString()!)));
                        if (node.TryGetProperty("properties", out var properties))
                            foreach (var property in properties.EnumerateObject())
                                if (value.TryGetProperty(property.Name, out var child))
                                    checks.Push((property.Value, child, PropertyPath(path, property.Name)));
                    }
                    if (value.ValueKind == JsonValueKind.Array && node.TryGetProperty("items", out var items))
                        for (var index = value.GetArrayLength() - 1; index >= 0; index--)
                            checks.Push((items, value[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"));
                }
                return (true, body, null);
            }
        }
    }

    private static bool IsType(string? type) =>
        type is "object" or "array" or "string" or "number" or "integer" or "boolean" or "null";

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && IsInteger(value.GetRawText()),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool IsInteger(string raw)
    {
        var parts = raw.Split('e', 'E');
        var coefficient = parts[0].TrimStart('-');
        var dot = coefficient.IndexOf('.');
        var fractionalDigits = dot < 0 ? 0 : coefficient.Length - dot - 1;
        var digits = coefficient.Replace(".", "");
        if (digits.Trim('0').Length == 0) return true;
        var trailingZeros = digits.Length - digits.TrimEnd('0').Length;
        var exponent = parts.Length == 1 ? System.Numerics.BigInteger.Zero
            : System.Numerics.BigInteger.Parse(parts[1], CultureInfo.InvariantCulture);
        return exponent + trailingZeros >= fractionalDigits;
    }

    private static string PropertyPath(string path, string name) => path + "[" + JsonSerializer.Serialize(name) + "]";
    private static string Message(string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get("Str.Assertions." + key), values);
}
