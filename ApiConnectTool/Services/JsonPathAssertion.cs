using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

internal static class JsonPathAssertion
{
    internal static (bool Passed, string Actual, string? Failure) Evaluate(Assertion assertion, string body)
    {
        if (!TryParsePath(assertion.Target, out var segments))
            return (false, "", Format("InvalidPath", assertion.Target));
        try
        {
            using var document = JsonDocument.Parse(body);
            var value = document.RootElement;
            foreach (var segment in segments)
            {
                if (segment.Index is { } index)
                {
                    if (value.ValueKind != JsonValueKind.Array || index >= value.GetArrayLength())
                        return (false, "", Format("MissingPath", assertion.Target));
                    value = value[index];
                }
                else
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment.Name!, out var property))
                        return (false, "", Format("MissingPath", assertion.Target));
                    value = property;
                }
            }
            var actual = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (assertion.Operator is AssertionOperator.GreaterThan or AssertionOperator.LessThan)
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number)
                    || !double.TryParse(assertion.ExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected) || !double.IsFinite(expected))
                    return (false, actual, DefaultStrings.Get("Str.Assertions.JsonNumberRequired"));
                if (value.TryGetDecimal(out var decimalActual)
                    && decimal.TryParse(assertion.ExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalExpected)
                    && (decimalActual != 0 || number == 0) && (decimalExpected != 0 || expected == 0))
                    return (assertion.Operator == AssertionOperator.GreaterThan ? decimalActual > decimalExpected : decimalActual < decimalExpected, actual, null);
                return (assertion.Operator == AssertionOperator.GreaterThan ? number > expected : number < expected, actual, null);
            }
            bool? passed = assertion.Operator switch
            {
                AssertionOperator.Exists => true,
                AssertionOperator.Equals => actual == assertion.ExpectedValue,
                AssertionOperator.NotEquals => actual != assertion.ExpectedValue,
                AssertionOperator.Contains => actual.Contains(assertion.ExpectedValue, StringComparison.Ordinal),
                _ => null
            };
            return (passed == true, actual, passed is null ? DefaultStrings.Get("Str.Assertions.UnsupportedOperator") : null);
        }
        catch (JsonException)
        {
            return (false, "", DefaultStrings.Get("Str.Assertions.InvalidJson"));
        }
    }

    // Validate the whole path before traversal, so missing values cannot hide malformed syntax.
    private static bool TryParsePath(string path, out List<(string? Name, int? Index)> segments)
    {
        segments = new();
        var offset = 0;
        var needsName = false;
        while (offset < path.Length)
        {
            if (path[offset] == '[' && !needsName)
            {
                var start = ++offset;
                while (offset < path.Length && char.IsAsciiDigit(path[offset])) offset++;
                if (start == offset || offset >= path.Length || path[offset] != ']'
                    || !int.TryParse(path.AsSpan(start, offset - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return false;
                segments.Add((null, index));
                offset++;
            }
            else
            {
                var start = offset;
                if (!(char.IsLetter(path[offset]) || path[offset] is '_' or '$')) return false;
                offset++;
                while (offset < path.Length && (char.IsLetterOrDigit(path[offset]) || path[offset] is '_' or '$' or '-')) offset++;
                segments.Add((path[start..offset], null));
                needsName = false;
            }
            if (offset == path.Length) return true;
            if (path[offset] == '.') { offset++; needsName = true; }
            else if (path[offset] != '[') return false;
        }
        return false;
    }

    private static string Format(string key, string value) =>
        string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get("Str.Assertions." + key), value);
}
