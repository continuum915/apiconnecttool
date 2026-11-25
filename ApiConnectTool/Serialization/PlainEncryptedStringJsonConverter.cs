using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiConnectTool.Models;

namespace ApiConnectTool.Serialization;

/// <summary>
/// Secret handling for the native export format - deliberately NOT
/// <see cref="EncryptedStringJsonConverter"/>, which DPAPI/Keychain-protects a value for the
/// exporting machine specifically and would be undecryptable the moment the file moves anywhere
/// else, defeating the portability an export file exists for. Writes plain text (or an empty
/// string, when secrets are excluded); reads whatever plain string is present directly, with no
/// "ENCRYPTED:" prefix expected or produced.
/// </summary>
public sealed class PlainEncryptedStringJsonConverter(bool includeSecrets) : JsonConverter<EncryptedString>
{
    public override EncryptedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, EncryptedString value, JsonSerializerOptions options) =>
        writer.WriteStringValue(includeSecrets ? value.Value ?? string.Empty : string.Empty);
}
