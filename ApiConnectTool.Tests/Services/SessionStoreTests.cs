using System;
using System.IO;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class SessionStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolSessionStoreTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmptySessionWithoutError()
    {
        var store = new SessionStore(_tempDirectory);

        var state = store.Load();

        Assert.Empty(state.OpenRequestIds);
        Assert.Null(state.ActiveRequestId);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOrderedIdsAndActiveId_AndWritesOnlyIdsAtomically()
    {
        var store = new SessionStore(_tempDirectory);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        store.Save(new SessionState([firstId, secondId, thirdId], secondId));
        var loaded = store.Load();

        Assert.Equal([firstId, secondId, thirdId], loaded.OpenRequestIds);
        Assert.Equal(secondId, loaded.ActiveRequestId);

        var sessionFile = Path.Combine(_tempDirectory, "session.json");
        Assert.True(File.Exists(sessionFile));
        Assert.False(File.Exists(sessionFile + ".tmp"));

        using var doc = JsonDocument.Parse(File.ReadAllText(sessionFile));
        var propertyNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            propertyNames.Add(property.Name);
        }

        // Request IDs plus one layout flag; never request content.
        Assert.Equal(new[] { "OpenRequestIds", "ActiveRequestId", "VariablesPaneVisible" }.Length, propertyNames.Count);
        Assert.Contains("VariablesPaneVisible", propertyNames);
        Assert.Contains("OpenRequestIds", propertyNames);
        Assert.Contains("ActiveRequestId", propertyNames);
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"openRequestIds\": \"not-an-array\"}")]
    public void Load_WhenFileIsCorrupt_ReturnsEmptySessionWithoutError(string corruptContent)
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "session.json"), corruptContent);
        var store = new SessionStore(_tempDirectory);

        var state = store.Load();

        Assert.Empty(state.OpenRequestIds);
        Assert.Null(state.ActiveRequestId);
    }
}
