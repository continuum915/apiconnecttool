using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Serialization
{
    public class HttpMethodJsonConverter : JsonConverter<HttpMethod>
    {
        public override HttpMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var methodStr = reader.GetString();
            return new HttpMethod(methodStr ?? "GET");
        }

        public override void Write(Utf8JsonWriter writer, HttpMethod value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Method);
        }
    }
}
