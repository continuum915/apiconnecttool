using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.Serialization
{
    /// <param name="preserveUndecryptable">When true, a value that failed to decrypt on read is
    /// written back with its exact original payload instead of as empty. Only the workspace file
    /// opts in: history snapshots keep writing such values as empty.</param>
    public class EncryptedStringJsonConverter(ISecretStore secretStore, bool preserveUndecryptable = false) : JsonConverter<EncryptedString>
    {
        // A prefix to quickly identify if the string is encrypted,
        // so we don't accidentally try to decrypt plain text (e.g., if the user manually edited the json)
        private const string Prefix = "ENCRYPTED:";

        public override EncryptedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (string.IsNullOrEmpty(text))
            {
                return new EncryptedString(string.Empty);
            }

            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                // Not encrypted (maybe manually entered in json)
                return new EncryptedString(text);
            }

            var payload = text[Prefix.Length..];
            if (!secretStore.TryUnprotect(payload, out var plaintext) || plaintext is null)
            {
                // Never silently discard the fact that a secret exists but could not be
                // recovered - that reads to the user as "the value was blank all along".
                return EncryptedString.Undecryptable(text);
            }

            return new EncryptedString(plaintext);
        }

        public override void Write(Utf8JsonWriter writer, EncryptedString value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value.Value))
            {
                if (preserveUndecryptable && value.FailedToDecrypt && value.UndecryptablePayload is { } storedPayload)
                {
                    writer.WriteStringValue(storedPayload);
                    return;
                }

                writer.WriteStringValue(string.Empty);
                return;
            }

            if (!secretStore.TryProtect(value.Value, out var protectedPayload) || protectedPayload is null)
            {
                throw new SecretProtectionException(
                    "A secret value could not be protected for storage on this platform. Refusing to save it as plaintext.");
            }

            writer.WriteStringValue(Prefix + protectedPayload);
        }
    }
}
