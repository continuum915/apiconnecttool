using System;

namespace ApiConnectTool.Models
{
    /// <summary>
    /// A string wrapper that indicates the value should be encrypted when serialized to JSON.
    /// In memory, the value is kept as plain text (or encrypted if preferred, but for UI binding plain text is easier).
    /// </summary>
    public struct EncryptedString
    {
        public string Value { get; set; }

        // Set when this value was loaded from a "ENCRYPTED:" payload that the current
        // platform's secret store could not decrypt (wrong machine/user, corrupt payload,
        // store unavailable). Value is empty in that case, but that emptiness must not be
        // confused with a genuinely empty/unset secret - callers should check this flag
        // before treating Value as authoritative.
        public bool FailedToDecrypt { get; set; }

        // The exact stored text (prefix included) that failed to decrypt, kept only so the
        // workspace can write it back unchanged and the secret stays recoverable on the machine
        // or profile that protected it. Never shown, logged, or used as a value. Assigning a new
        // EncryptedString drops it, so a user's edit always wins.
        internal string? UndecryptablePayload { get; private init; }

        public EncryptedString(string value)
        {
            Value = value;
        }

        public static EncryptedString Undecryptable() => new(string.Empty) { FailedToDecrypt = true };

        internal static EncryptedString Undecryptable(string storedPayload) =>
            new(string.Empty) { FailedToDecrypt = true, UndecryptablePayload = storedPayload };

        public static implicit operator string(EncryptedString e) => e.Value;
        public static implicit operator EncryptedString(string s) => new EncryptedString(s);

        public override string ToString() => Value ?? string.Empty;
    }
}
