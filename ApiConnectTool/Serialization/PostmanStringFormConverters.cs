using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Serialization;

// Postman v2.1 allows a request's url, and an item's whole request, to be a plain string
// instead of an object. These converters are applied to the properties only, so reading the
// object form falls through to the default DTO deserialization without recursing.

internal sealed class PostmanUrlConverter : JsonConverter<PostmanUrlDto>
{
    public override PostmanUrlDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new PostmanUrlDto { Raw = reader.GetString() ?? string.Empty },
            JsonTokenType.Null => null,
            _ => JsonSerializer.Deserialize<PostmanUrlDto>(ref reader, options)
        };

    public override void Write(Utf8JsonWriter writer, PostmanUrlDto value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

internal sealed class PostmanRequestConverter : JsonConverter<PostmanRequestDto>
{
    public override PostmanRequestDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new PostmanRequestDto { Method = "GET", Url = new PostmanUrlDto { Raw = reader.GetString() ?? string.Empty } },
            JsonTokenType.Null => null,
            _ => JsonSerializer.Deserialize<PostmanRequestDto>(ref reader, options)
        };

    public override void Write(Utf8JsonWriter writer, PostmanRequestDto value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
