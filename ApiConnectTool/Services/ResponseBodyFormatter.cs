using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ApiConnectTool.Services;

public static class ResponseBodyFormatter
{
    public static bool ContainsFormatCharacters(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) return true;
        return false;
    }

    public static string Pretty(string text, string contentType)
    {
        try
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(text);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                    document.WriteTo(writer);
                return EscapeFormatCharacters(Encoding.UTF8.GetString(stream.ToArray()));
            }
            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                using var input = new StringReader(text);
                using var reader = XmlReader.Create(input, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                return XDocument.Load(reader).ToString();
            }
        }
        catch (Exception error) when (error is JsonException or XmlException or ArgumentException or InvalidOperationException)
        {
            // The original decoded body is always usable when formatting fails.
        }
        return text;
    }

    private static string EscapeFormatCharacters(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) output.Append(rune.ToString());
            else foreach (var codeUnit in rune.ToString()) output.Append("\\u").Append(((int)codeUnit).ToString("X4", CultureInfo.InvariantCulture));
        }
        return output.ToString();
    }
}
