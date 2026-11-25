#pragma warning disable CA1416
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Serialization
{
    public class EncryptedStringJsonConverterTests
    {
        private static JsonSerializerOptions OptionsWith(ISecretStore secretStore) => new()
        {
            Converters = { new EncryptedStringJsonConverter(secretStore) }
        };

        private static readonly JsonSerializerOptions Options = OptionsWith(new WindowsDpapiSecretStore());

        [Fact]
        public void Write_EmptyValue_SerializesEmptyString()
        {
            var es = new EncryptedString(string.Empty);
            var json = JsonSerializer.Serialize(es, Options);
            Assert.Equal("\"\"", json);
        }

        [Fact]
        public void Read_EmptyString_ReturnsEmptyEncryptedString()
        {
            var result = JsonSerializer.Deserialize<EncryptedString>("\"\"", Options);
            Assert.Equal(string.Empty, result.Value);
            Assert.False(result.FailedToDecrypt);
        }

        [Fact]
        public void Read_PlainTextWithoutPrefix_ReturnsAsIs()
        {
            // Text that doesn't start with ENCRYPTED: is returned as plain text
            var result = JsonSerializer.Deserialize<EncryptedString>("\"plain-text-value\"", Options);
            Assert.Equal("plain-text-value", result.Value);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void Write_NonEmptyValue_ProducesEncryptedPrefix()
        {
            var es = new EncryptedString("my-secret-token");
            var json = JsonSerializer.Serialize(es, Options);
            // Remove the surrounding quotes from the JSON string
            var raw = json.Trim('"');
            Assert.StartsWith("ENCRYPTED:", raw);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void Write_NonEmptyValue_ProducesValidBase64AfterPrefix()
        {
            var es = new EncryptedString("my-secret-token");
            var json = JsonSerializer.Serialize(es, Options);
            // Deserialize back to a raw string to properly unescape any JSON escape sequences
            var rawValue = JsonSerializer.Deserialize<string>(json)!;
            var base64Part = rawValue.Substring("ENCRYPTED:".Length);
            // Should not throw
            var bytes = System.Convert.FromBase64String(base64Part);
            Assert.NotEmpty(bytes);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void RoundTrip_EncryptsAndDecrypts()
        {
            var original = new EncryptedString("super-secret-value");
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<EncryptedString>(json, Options);
            Assert.Equal(original.Value, result.Value);
            Assert.False(result.FailedToDecrypt);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void RoundTrip_EmptyValue_RemainsEmpty()
        {
            var original = new EncryptedString(string.Empty);
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<EncryptedString>(json, Options);
            Assert.Equal(string.Empty, result.Value);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void RoundTrip_SpecialCharacters_PreservesValue()
        {
            var original = new EncryptedString("p@$$w0rd!#%^&*()");
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<EncryptedString>(json, Options);
            Assert.Equal(original.Value, result.Value);
        }

        [Fact]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void RoundTrip_UnicodeValue_PreservesValue()
        {
            var original = new EncryptedString("こんにちは世界");
            var json = JsonSerializer.Serialize(original, Options);
            var result = JsonSerializer.Deserialize<EncryptedString>(json, Options);
            Assert.Equal(original.Value, result.Value);
        }

        [Fact]
        public void Write_WhenSecretStoreCannotProtect_ThrowsRatherThanWritingPlaintext()
        {
            var options = OptionsWith(new FailingSecretStore());
            var es = new EncryptedString("super-secret-value");

            Assert.Throws<SecretProtectionException>(() => JsonSerializer.Serialize(es, options));
        }

        [Fact]
        public void Read_WhenPayloadCannotBeDecrypted_ReturnsUndecryptableSentinelNotEmptySecret()
        {
            var options = OptionsWith(new FailingSecretStore());

            var result = JsonSerializer.Deserialize<EncryptedString>("\"ENCRYPTED:whatever\"", options);

            Assert.True(result.FailedToDecrypt);
            Assert.Equal(string.Empty, result.Value);
        }

        [Fact]
        public void Write_UndecryptableValue_WritesOriginalPayloadOnlyWhenPreserving()
        {
            var read = JsonSerializer.Deserialize<EncryptedString>("\"ENCRYPTED:whatever\"", OptionsWith(new FailingSecretStore()));
            var preserving = new JsonSerializerOptions { Converters = { new EncryptedStringJsonConverter(new FailingSecretStore(), preserveUndecryptable: true) } };

            Assert.Equal("\"ENCRYPTED:whatever\"", JsonSerializer.Serialize(read, preserving));
            Assert.Equal("\"\"", JsonSerializer.Serialize(read, OptionsWith(new FailingSecretStore())));
        }

        [Fact]
        public void Undecryptable_NeverExposesPayloadThroughValueOrToString()
        {
            var read = JsonSerializer.Deserialize<EncryptedString>("\"ENCRYPTED:whatever\"", OptionsWith(new FailingSecretStore()));

            Assert.Equal(string.Empty, read.Value);
            Assert.Equal(string.Empty, read.ToString());
            Assert.Equal(string.Empty, (string)read);
        }

        private sealed class FailingSecretStore : ISecretStore
        {
            public bool TryProtect(string plaintext, out string? protectedPayload)
            {
                protectedPayload = null;
                return false;
            }

            public bool TryUnprotect(string protectedPayload, out string? plaintext)
            {
                plaintext = null;
                return false;
            }
        }
    }
}
