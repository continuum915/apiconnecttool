using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public static class IterationDataParser
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Parse(string format, string content) =>
        format.TrimStart('.').ToLowerInvariant() switch
        {
            "csv" => ParseCsv(content),
            "json" => ParseJson(content),
            _ => throw new FormatException(Message("Format"))
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseJson(string content)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content); }
        catch (JsonException error) { throw new FormatException(Message("JsonSyntax", error.LineNumber + 1), error); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new FormatException(Message("JsonArray"));
            var rows = new List<IReadOnlyDictionary<string, string>>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                index++;
                if (element.ValueKind != JsonValueKind.Object)
                    throw new FormatException(Message("JsonObject", index));
                var row = new Dictionary<string, string>();
                foreach (var property in element.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(property.Name) || row.ContainsKey(property.Name))
                        throw new FormatException(Message("JsonKey", index));
                    row[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString()!,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => throw new FormatException(Message("JsonScalar", index))
                    };
                }
                rows.Add(row);
            }
            return rows;
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseCsv(string content)
    {
        var records = new List<(int Line, List<string> Fields)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var quoted = false;
        var closedQuote = false;
        var fieldStarted = false;
        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            closedQuote = false;
        }
        void EndRecord()
        {
            EndField();
            records.Add((recordLine, new List<string>(fields)));
            fields.Clear();
            recordLine = line + 1;
        }
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closedQuote = true; }
                }
                else
                {
                    field.Append(c);
                    if (c == '\n' || c == '\r' && (i + 1 >= content.Length || content[i + 1] != '\n')) line++;
                }
            }
            else if (c == ',') EndField();
            else if (c == '\r' || c == '\n')
            {
                EndRecord();
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                line++;
            }
            else if (c == '"' && !fieldStarted && !closedQuote) { quoted = true; fieldStarted = true; }
            else if (closedQuote || c == '"') throw new FormatException(Message("CsvQuote", line));
            else { field.Append(c); fieldStarted = true; }
        }
        if (quoted) throw new FormatException(Message("CsvUnclosed", recordLine));
        if (fieldStarted || closedQuote || fields.Count > 0) EndRecord();
        if (records.Count == 0 || records[0].Fields.Count == 1 && records[0].Fields[0].Length == 0)
            throw new FormatException(Message("CsvHeaderMissing"));
        var headers = records[0].Fields;
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Count)
            throw new FormatException(Message("CsvHeaderInvalid"));
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count != headers.Count)
                throw new FormatException(Message("CsvFieldCount", record.Line, record.Fields.Count, headers.Count));
            rows.Add(headers.Select((header, index) => (header, index))
                .ToDictionary(pair => pair.header, pair => record.Fields[pair.index]));
        }
        return rows;
    }

    private static string Message(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, DefaultStrings.Get("Str.Runner.Data." + key), values);
}
