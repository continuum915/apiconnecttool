using System.Collections.ObjectModel;
using System.Net.Http;
using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Models
{
    public class RequestItemTests
    {
        [Fact]
        public void DefaultName_IsEmpty()
        {
            var item = new RequestItem();
            Assert.Equal(string.Empty, item.Name);
        }

        [Fact]
        public void DefaultMethod_IsGet()
        {
            var item = new RequestItem();
            Assert.Equal(HttpMethod.Get, item.Method);
        }

        [Fact]
        public void DefaultUrl_IsEmpty()
        {
            var item = new RequestItem();
            Assert.Equal(string.Empty, item.Url);
        }

        [Fact]
        public void DefaultAuthToken_IsEmpty()
        {
            var item = new RequestItem();
            Assert.Equal(string.Empty, item.AuthToken.Value);
        }

        [Fact]
        public void DefaultAuth_IsNotNull()
        {
            var item = new RequestItem();
            Assert.NotNull(item.Auth);
        }

        [Fact]
        public void DefaultAuth_TypeIsInherit()
        {
            var item = new RequestItem();
            Assert.Equal(AuthType.Inherit, item.Auth.Type);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var item = new RequestItem
            {
                Name = "Get Users",
                Method = HttpMethod.Post,
                Url = "https://api.example.com/users",
                AuthToken = "secret"
            };

            Assert.Equal("Get Users", item.Name);
            Assert.Equal(HttpMethod.Post, item.Method);
            Assert.Equal("https://api.example.com/users", item.Url);
            Assert.Equal("secret", item.AuthToken.Value);
        }
    }

    public class RequestCollectionTests
    {
        [Fact]
        public void DefaultName_IsEmpty()
        {
            var col = new RequestCollection();
            Assert.Equal(string.Empty, col.Name);
        }

        [Fact]
        public void DefaultRequests_IsEmpty()
        {
            var col = new RequestCollection();
            Assert.Empty(col.Requests);
        }

        [Fact]
        public void Requests_CanHaveItemsAdded()
        {
            var col = new RequestCollection();
            col.Requests.Add(new RequestItem { Name = "Test" });
            Assert.Single(col.Requests);
        }
    }

    public class WorkspaceTests
    {
        [Fact]
        public void DefaultCollections_IsEmpty()
        {
            var ws = new Workspace();
            Assert.Empty(ws.Collections);
        }

        [Fact]
        public void Collections_CanHaveItemsAdded()
        {
            var ws = new Workspace();
            ws.Collections.Add(new RequestCollection { Name = "My API" });
            Assert.Single(ws.Collections);
        }
    }
}
