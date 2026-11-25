using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Models
{
    public class EncryptedStringTests
    {
        [Fact]
        public void Constructor_StoresValue()
        {
            var es = new EncryptedString("hello");
            Assert.Equal("hello", es.Value);
        }

        [Fact]
        public void Constructor_Empty_StoresEmpty()
        {
            var es = new EncryptedString(string.Empty);
            Assert.Equal(string.Empty, es.Value);
        }

        [Fact]
        public void ImplicitConversion_FromString()
        {
            EncryptedString es = "secret";
            Assert.Equal("secret", es.Value);
        }

        [Fact]
        public void ImplicitConversion_ToString()
        {
            var es = new EncryptedString("token");
            string s = es;
            Assert.Equal("token", s);
        }

        [Fact]
        public void ToString_ReturnsValue()
        {
            var es = new EncryptedString("abc");
            Assert.Equal("abc", es.ToString());
        }

        [Fact]
        public void ToString_WhenValueIsNull_ReturnsEmpty()
        {
            // EncryptedString is a struct; Value is a string? so null is possible
            var es = new EncryptedString(null!);
            Assert.Equal(string.Empty, es.ToString());
        }

        [Fact]
        public void TwoInstances_SameValue_AreStructurallyEqual()
        {
            var a = new EncryptedString("test");
            var b = new EncryptedString("test");
            Assert.Equal(a.Value, b.Value);
        }
    }
}
