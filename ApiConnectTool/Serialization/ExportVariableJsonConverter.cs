using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiConnectTool.Models;

namespace ApiConnectTool.Serialization;

public sealed class ExportVariableJsonConverter(bool includeSecrets) : JsonConverter<EnvironmentVariable>
{
    public override EnvironmentVariable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var readOptions = new JsonSerializerOptions(options);
        for (var i = readOptions.Converters.Count - 1; i >= 0; i--)
            if (readOptions.Converters[i] is ExportVariableJsonConverter) readOptions.Converters.RemoveAt(i);
        return JsonSerializer.Deserialize<EnvironmentVariable>(ref reader, readOptions)!;
    }
    public override void Write(Utf8JsonWriter writer, EnvironmentVariable variable, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new {
            variable.Id, variable.Key, variable.IsSecret, variable.IsEnabled, variable.HasCurrentValue,
            InitialValue = variable.IsSecret && !includeSecrets ? string.Empty : variable.InitialValue.Value,
            CurrentValue = variable.IsSecret && !includeSecrets ? string.Empty : variable.CurrentValue.Value
        }, options);
}
