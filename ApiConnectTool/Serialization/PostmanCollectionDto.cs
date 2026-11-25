using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Serialization;

// Wire-format DTOs for Postman Collection v2.1 - deliberately a thin subset (not the full
// Postman schema) covering exactly what PostmanCollectionConverter maps to/from ApiConnectTool's domain
// models. Property names use camelCase (via the converter's JsonSerializerOptions) to match
// Postman's actual field names.

internal sealed class PostmanCollectionDto
{
    public PostmanInfoDto Info { get; set; } = new();

    // Nullable (no default) so a document missing "item" entirely is distinguishable from one
    // with an explicit empty array - the former is not a valid collection.
    public List<PostmanItemDto>? Item { get; set; }
    public PostmanAuthDto? Auth { get; set; }
    public List<PostmanVariableDto>? Variable { get; set; }
    public List<PostmanEventDto>? Event { get; set; }
}

internal sealed class PostmanInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";
}

internal sealed class PostmanItemDto
{
    public string Name { get; set; } = string.Empty;

    // Presence (even as an empty array) marks this item as a folder rather than a request -
    // a real Postman request item never has this property at all.
    public List<PostmanItemDto>? Item { get; set; }

    [JsonConverter(typeof(PostmanRequestConverter))]
    public PostmanRequestDto? Request { get; set; }
    public PostmanAuthDto? Auth { get; set; }
    public List<PostmanEventDto>? Event { get; set; }
}

internal sealed class PostmanRequestDto
{
    public string Method { get; set; } = "GET";
    public List<PostmanHeaderDto> Header { get; set; } = new();
    [JsonConverter(typeof(PostmanUrlConverter))]
    public PostmanUrlDto? Url { get; set; }
    public PostmanBodyDto? Body { get; set; }
    public PostmanAuthDto? Auth { get; set; }
}

internal sealed class PostmanHeaderDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Disabled { get; set; }
}

internal sealed class PostmanUrlDto
{
    public string Raw { get; set; } = string.Empty;
    public List<PostmanQueryParamDto> Query { get; set; } = new();

    // Path variables (":id" segments). Import-only: ApiConnectTool inlines them, so export leaves this null.
    public List<PostmanPathVariableDto>? Variable { get; set; }
}

internal sealed class PostmanPathVariableDto
{
    public string Key { get; set; } = string.Empty;

    // Postman allows any JSON type here; strings, numbers, and booleans are inlined.
    public JsonElement Value { get; set; }
}

internal sealed class PostmanQueryParamDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Disabled { get; set; }
}

internal sealed class PostmanBodyDto
{
    public string? Mode { get; set; }
    public string? Raw { get; set; }
    public List<PostmanFormParamDto>? Urlencoded { get; set; }
    public List<PostmanFormParamDto>? Formdata { get; set; }
    public PostmanBodyOptionsDto? Options { get; set; }
    public PostmanFileDto? File { get; set; }
}

internal sealed class PostmanFormParamDto
{
    public string? Src { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Disabled { get; set; }
    public string? Type { get; set; }
}

internal sealed class PostmanBodyOptionsDto
{
    public PostmanRawOptionsDto? Raw { get; set; }
}

internal sealed class PostmanRawOptionsDto
{
    public string? Language { get; set; }
}

internal sealed class PostmanFileDto
{
    public string? Src { get; set; }
}

internal sealed class PostmanAuthDto
{
    public string Type { get; set; } = "noauth";
    public List<PostmanAuthParamDto>? Bearer { get; set; }
    public List<PostmanAuthParamDto>? Basic { get; set; }
    public List<PostmanAuthParamDto>? Apikey { get; set; }
    public List<PostmanAuthParamDto>? Oauth2 { get; set; }
}

internal sealed class PostmanAuthParamDto
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

internal sealed class PostmanVariableDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool Disabled { get; set; }
}

internal sealed class PostmanEventDto
{
    public string Listen { get; set; } = string.Empty;
    public JsonElement Script { get; set; }
}
