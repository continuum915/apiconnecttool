using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ApiConnectTool.Serialization;
using Xunit;

namespace ApiConnectTool.Tests.Serialization
{
    public class HttpMethodJsonConverterTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Converters = { new HttpMethodJsonConverter() }
        };

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public void Write_HttpMethod_SerializesMethodString(string methodStr)
        {
            var method = new HttpMethod(methodStr);
            // Wrap in an object so we can use JsonSerializer
            var json = JsonSerializer.Serialize(method, Options);
            Assert.Equal($"\"{methodStr}\"", json);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("DELETE")]
        public void Read_MethodString_DeserializesHttpMethod(string methodStr)
        {
            var json = $"\"{methodStr}\"";
            var method = JsonSerializer.Deserialize<HttpMethod>(json, Options);
            Assert.Equal(methodStr, method!.Method);
        }

        [Fact]
        public void RoundTrip_Get_PreservesMethod()
        {
            var original = HttpMethod.Get;
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<HttpMethod>(json, Options);
            Assert.Equal(original.Method, result!.Method);
        }

        [Fact]
        public void RoundTrip_Post_PreservesMethod()
        {
            var original = HttpMethod.Post;
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<HttpMethod>(json, Options);
            Assert.Equal(original.Method, result!.Method);
        }

        [Fact]
        public void Write_UsesConverterDirectly()
        {
            var converter = new HttpMethodJsonConverter();
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            converter.Write(writer, HttpMethod.Patch, Options);
            writer.Flush();

            var json = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Equal("\"PATCH\"", json);
        }

        [Fact]
        public void Read_EmptyString_ThrowsException()
        {
            // HttpMethod does not allow empty strings; the converter propagates this
            var json = "\"\"";
            Assert.ThrowsAny<Exception>(() =>
                JsonSerializer.Deserialize<HttpMethod>(json, Options));
        }
    }
}
