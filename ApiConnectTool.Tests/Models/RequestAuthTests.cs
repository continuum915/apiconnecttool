using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Models
{
    public class RequestAuthTests
    {
        [Fact]
        public void DefaultType_IsNone()
        {
            var auth = new RequestAuth();
            Assert.Equal(AuthType.None, auth.Type);
        }

        [Fact]
        public void DefaultToken_IsEmpty()
        {
            var auth = new RequestAuth();
            Assert.Equal(string.Empty, auth.Token.Value);
        }

        [Fact]
        public void DefaultUsername_IsEmpty()
        {
            var auth = new RequestAuth();
            Assert.Equal(string.Empty, auth.Username);
        }

        [Fact]
        public void DefaultPassword_IsEmpty()
        {
            var auth = new RequestAuth();
            Assert.Equal(string.Empty, auth.Password.Value);
        }

        [Fact]
        public void DefaultApiKeyHeaderName_IsEmpty()
        {
            var auth = new RequestAuth();
            Assert.Equal(string.Empty, auth.ApiKeyHeaderName);
        }

        [Fact]
        public void DefaultApiKeyQueryParam_IsEmpty()
        {
            var auth = new RequestAuth();
            Assert.Equal(string.Empty, auth.ApiKeyQueryParam);
        }

        [Fact]
        public void AuthType_HasSixValues()
        {
            var values = System.Enum.GetValues<AuthType>();
            Assert.Equal(6, values.Length);
        }

        [Fact]
        public void AuthType_None_IsZero()
        {
            Assert.Equal(0, (int)AuthType.None);
        }

        [Fact]
        public void AuthType_Bearer_IsOne()
        {
            Assert.Equal(1, (int)AuthType.Bearer);
        }

        [Fact]
        public void AuthType_Basic_IsTwo()
        {
            Assert.Equal(2, (int)AuthType.Basic);
        }

        [Fact]
        public void AuthType_ApiKey_IsThree()
        {
            Assert.Equal(3, (int)AuthType.ApiKey);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var auth = new RequestAuth
            {
                Type = AuthType.Bearer,
                Token = "my-token",
                Username = "user",
                Password = "pass",
                ApiKeyHeaderName = "X-Api-Key",
                ApiKeyQueryParam = "api_key"
            };

            Assert.Equal(AuthType.Bearer, auth.Type);
            Assert.Equal("my-token", auth.Token.Value);
            Assert.Equal("user", auth.Username);
            Assert.Equal("pass", auth.Password.Value);
            Assert.Equal("X-Api-Key", auth.ApiKeyHeaderName);
            Assert.Equal("api_key", auth.ApiKeyQueryParam);
        }
    }
}
