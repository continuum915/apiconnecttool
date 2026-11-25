using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Models
{
    public class KeyValueItemTests
    {
        [Fact]
        public void DefaultValues()
        {
            var item = new KeyValueItem();
            Assert.True(item.IsEnabled);
            Assert.Equal(string.Empty, item.Key);
            Assert.Equal(string.Empty, item.Value);
            Assert.Equal(string.Empty, item.Description);
        }

        [Fact]
        public void Key_Set_RaisesPropertyChanged()
        {
            var item = new KeyValueItem();
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.Key = "X-Api-Key";

            Assert.Contains("Key", raised);
        }

        [Fact]
        public void Value_Set_RaisesPropertyChanged()
        {
            var item = new KeyValueItem();
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.Value = "abc123";

            Assert.Contains("Value", raised);
        }

        [Fact]
        public void Description_Set_RaisesPropertyChanged()
        {
            var item = new KeyValueItem();
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.Description = "An API key";

            Assert.Contains("Description", raised);
        }

        [Fact]
        public void IsEnabled_Set_RaisesPropertyChanged()
        {
            var item = new KeyValueItem();
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.IsEnabled = false;

            Assert.Contains("IsEnabled", raised);
        }

        [Fact]
        public void IsEnabled_CanBeSetToFalse()
        {
            var item = new KeyValueItem { IsEnabled = false };
            Assert.False(item.IsEnabled);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var item = new KeyValueItem
            {
                Key = "Authorization",
                Value = "Bearer token",
                Description = "Auth header",
                IsEnabled = false
            };

            Assert.Equal("Authorization", item.Key);
            Assert.Equal("Bearer token", item.Value);
            Assert.Equal("Auth header", item.Description);
            Assert.False(item.IsEnabled);
        }
    }
}
