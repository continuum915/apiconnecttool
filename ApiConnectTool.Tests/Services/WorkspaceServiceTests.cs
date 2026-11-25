using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class WorkspaceServiceTests : IDisposable
{
    [Fact]
    public void ExplicitEmptyCurrentValuePersistsWithoutChangingLegacyFallback()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"),
            """{"SchemaVersion":7,"GlobalVariables":[{"Key":"key","InitialValue":"initial","CurrentValue":""}]}""");
        var service = new WorkspaceService(_tempDirectory);
        var workspace = service.LoadWorkspace();
        Assert.Equal("initial", VariableScopeBuilder.Build(workspace).Variables["key"]);
        new ScriptVariableScopes(workspace, null, null).Apply("globals", "key", "");
        service.SaveWorkspace(workspace);
        Assert.Equal("", VariableScopeBuilder.Build(service.LoadWorkspace()).Variables["key"]);
    }
    [Fact]
    public void PreviousAuthSchemaPreservesExplicitNoneAndBearer()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"),
            """{"SchemaVersion":6,"Collections":[{"Requests":[{"Auth":{"Type":0}},{"Auth":{"Type":1,"Token":"old"}},{}]}]}""");
        var loaded = new WorkspaceService(_tempDirectory).LoadWorkspace();
        Assert.Equal(new[] { AuthType.None, AuthType.Bearer, AuthType.None }, loaded.Collections[0].Requests.Select(request => request.Auth.Type));
        Assert.Equal(AuthType.None, loaded.Collections[0].Auth.Type);
    }
    [Fact]
    public void GlobalAndCollectionVariablesMigrateAndPersistProtectedValues()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), """{"SchemaVersion":5,"Collections":[{}]}""");
        var service = new WorkspaceService(_tempDirectory);
        var workspace = service.LoadWorkspace();
        Assert.Empty(workspace.GlobalVariables);
        Assert.Empty(workspace.Collections[0].Variables);
        workspace.GlobalVariables.Add(new() { Key = "global", IsSecret = true, InitialValue = "global-private" });
        workspace.Collections[0].Variables.Add(new() { Key = "collection", IsSecret = true, CurrentValue = "collection-private" });
        service.SaveWorkspace(workspace);
        var stored = File.ReadAllText(Path.Combine(_tempDirectory, "workspace.json"));
        Assert.DoesNotContain("global-private", stored);
        Assert.DoesNotContain("collection-private", stored);
        var loaded = service.LoadWorkspace();
        Assert.Equal("global-private", loaded.GlobalVariables[0].InitialValue.Value);
        Assert.Equal("collection-private", loaded.Collections[0].Variables[0].CurrentValue.Value);
    }
    [Fact]
    public void PreviousSchemaLoadsDefaultSettingsAndSavedSettingsRoundTrip()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"),
            """{"SchemaVersion":4,"Collections":[{"Requests":[{"Name":"old"}]}]}""");
        var service = new WorkspaceService(_tempDirectory);
        var workspace = service.LoadWorkspace();
        var request = workspace.Collections[0].Requests[0];
        Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
        Assert.Equal(30, request.TimeoutSeconds);
        Assert.True(request.FollowRedirects);
        Assert.Empty(request.ProxyUrl);
        Assert.False(request.IgnoreCertificateErrors);
        request.TimeoutSeconds = 90;
        request.FollowRedirects = false;
        request.ProxyUrl = "http://localhost:8888";
        request.IgnoreCertificateErrors = true;
        service.SaveWorkspace(workspace);
        var reopened = service.LoadWorkspace().Collections[0].Requests[0];
        Assert.Equal(90, reopened.TimeoutSeconds);
        Assert.False(reopened.FollowRedirects);
        Assert.Equal("http://localhost:8888", reopened.ProxyUrl);
        Assert.True(reopened.IgnoreCertificateErrors);
    }
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCompleteRequestAndStableIds()
    {
        var service = new WorkspaceService(_tempDirectory);
        var request = new RequestItem
        {
            Id = Guid.NewGuid(),
            Name = "Create pet",
            Url = "https://api.example.test/pets",
            BodyMode = RequestBodyMode.UrlEncoded,
            BodyContentType = "application/x-www-form-urlencoded",
            Body = "name=Mochi",
            BodyFilePath = "C:\\fixtures\\pet.txt",
            QueryParams = new() { new KeyValueItem { Key = "dryRun", Value = "true" } },
            Headers = new() { new KeyValueItem { Key = "Accept", Value = "application/json" } },
            FormData = new() { new KeyValueItem { Key = "name", Value = "Mochi" } },
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "secret" }
        };
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Id = Guid.NewGuid(), Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };

        service.SaveWorkspace(workspace);
        var loaded = service.LoadWorkspace();
        var loadedRequest = loaded.Collections[0].Requests[0];

        Assert.Equal(workspace.Id, loaded.Id);
        Assert.Equal(workspace.Collections[0].Id, loaded.Collections[0].Id);
        Assert.Equal(request.Id, loadedRequest.Id);
        Assert.Equal(RequestBodyMode.UrlEncoded, loadedRequest.BodyMode);
        Assert.Equal("name=Mochi", loadedRequest.Body);
        Assert.Equal("dryRun", loadedRequest.QueryParams[0].Key);
        Assert.Equal("Accept", loadedRequest.Headers[0].Key);
        Assert.Equal("name", loadedRequest.FormData[0].Key);
        Assert.Equal(AuthType.Bearer, loadedRequest.Auth.Type);
        Assert.Equal("secret", loadedRequest.Auth.Token.Value);
    }

    [Fact]
    public void LoadWorkspace_UsesBackupWhenPrimaryIsCorrupt()
    {
        var service = new WorkspaceService(_tempDirectory);
        service.SaveWorkspace(new Workspace { Collections = new() { new RequestCollection { Name = "First" } } });
        service.SaveWorkspace(new Workspace { Collections = new() { new RequestCollection { Name = "Second" } } });
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), "not json");

        var loaded = service.LoadWorkspace();

        Assert.Equal("First", loaded.Collections[0].Name);
        Assert.NotNull(service.LastLoadError);
    }

    [Fact]
    public void LoadWorkspace_UnreadableFileWithoutBackup_KeepsACopyThatLaterSavesCannotOverwrite()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), "{ damaged but maybe recoverable");
        var service = new WorkspaceService(_tempDirectory);

        var loaded = service.LoadWorkspace();
        service.SaveWorkspace(loaded);
        service.SaveWorkspace(loaded);

        Assert.Empty(loaded.Collections);
        var preserved = Assert.Single(Directory.GetFiles(_tempDirectory, "workspace.unreadable-*.json"));
        Assert.Equal("{ damaged but maybe recoverable", File.ReadAllText(preserved));
        Assert.Contains(preserved, service.LastLoadError);
    }

    [Fact]
    public void LoadWorkspace_BackupRestore_AlsoKeepsACopyOfTheUnreadableFile()
    {
        var service = new WorkspaceService(_tempDirectory);
        service.SaveWorkspace(new Workspace { Collections = new() { new RequestCollection { Name = "First" } } });
        service.SaveWorkspace(new Workspace { Collections = new() { new RequestCollection { Name = "Second" } } });
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), "not json");

        service.LoadWorkspace();

        var preserved = Assert.Single(Directory.GetFiles(_tempDirectory, "workspace.unreadable-*.json"));
        Assert.Equal("not json", File.ReadAllText(preserved));
    }

    [Fact]
    public void LoadWorkspace_ReadableFile_DoesNotCreateAnUnreadableCopy()
    {
        var service = new WorkspaceService(_tempDirectory);
        service.SaveWorkspace(new Workspace { Collections = new() { new RequestCollection { Name = "First" } } });

        service.LoadWorkspace();

        Assert.Empty(Directory.GetFiles(_tempDirectory, "workspace.unreadable-*.json"));
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEnvironmentWithSecretVariable()
    {
        var service = new WorkspaceService(_tempDirectory);
        var environment = new RequestEnvironment
        {
            Id = Guid.NewGuid(),
            Name = "Staging",
            IsProduction = false,
            Variables = new()
            {
                new EnvironmentVariable { Key = "apiKey", InitialValue = "shared-default", CurrentValue = "secret-value", IsSecret = true },
                new EnvironmentVariable { Key = "baseUrl", InitialValue = "https://staging.example.test", IsSecret = false }
            }
        };
        var workspace = new Workspace
        {
            Environments = new() { environment },
            SelectedEnvironmentId = environment.Id
        };

        service.SaveWorkspace(workspace);
        var loaded = service.LoadWorkspace();

        Assert.Equal(environment.Id, loaded.SelectedEnvironmentId);
        var loadedEnvironment = Assert.Single(loaded.Environments);
        Assert.Equal("Staging", loadedEnvironment.Name);
        var secretVariable = loadedEnvironment.Variables.Single(v => v.Key == "apiKey");
        Assert.Equal("secret-value", secretVariable.CurrentValue.Value);
        Assert.False(secretVariable.CurrentValue.FailedToDecrypt);
        var plainVariable = loadedEnvironment.Variables.Single(v => v.Key == "baseUrl");
        Assert.Equal("https://staging.example.test", plainVariable.InitialValue.Value);

        var rawJson = File.ReadAllText(Path.Combine(_tempDirectory, "workspace.json"));
        Assert.Contains("ENCRYPTED:", rawJson);
        Assert.DoesNotContain("secret-value", rawJson);
    }

    [Fact]
    public void LoadWorkspace_DefaultsMissingEnvironmentsOnLegacySchema()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), """
            { "Id": "5a1b6a8e-6b8a-4b8a-9b8a-6b8a4b8a9b8a", "SchemaVersion": 1, "Collections": [] }
            """);
        var service = new WorkspaceService(_tempDirectory);

        var loaded = service.LoadWorkspace();

        Assert.NotNull(loaded.Environments);
        Assert.Empty(loaded.Environments);
        Assert.Null(loaded.SelectedEnvironmentId);
        Assert.Equal(Workspace.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void LoadWorkspace_ClearsSelectedEnvironmentIdWhenEnvironmentNoLongerExists()
    {
        var service = new WorkspaceService(_tempDirectory);
        var danglingId = Guid.NewGuid();
        service.SaveWorkspace(new Workspace { SelectedEnvironmentId = danglingId });

        var loaded = service.LoadWorkspace();

        Assert.Null(loaded.SelectedEnvironmentId);
    }

    [Fact]
    public void LoadWorkspace_MigratesLegacyIdsAndBearerToken()
    {
        var service = new WorkspaceService(_tempDirectory);
        var legacy = new Workspace
        {
            Id = Guid.Empty,
            SchemaVersion = 0,
            Collections = new() { new RequestCollection { Id = Guid.Empty, Requests = new() { new RequestItem { Id = Guid.Empty, AuthToken = "legacy-token" } } } }
        };

        service.SaveWorkspace(legacy);
        var loaded = service.LoadWorkspace();

        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.NotEqual(Guid.Empty, loaded.Collections[0].Id);
        Assert.NotEqual(Guid.Empty, loaded.Collections[0].Requests[0].Id);
        Assert.Equal(AuthType.Bearer, loaded.Collections[0].Requests[0].Auth.Type);
        Assert.Equal("legacy-token", loaded.Collections[0].Requests[0].Auth.Token.Value);
    }

    [Fact]
    public void LoadWorkspace_Schema2Workspace_BackfillsSortOrderFromExistingListPosition()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "workspace.json"), """
            {
              "Id": "5a1b6a8e-6b8a-4b8a-9b8a-6b8a4b8a9b8a",
              "SchemaVersion": 2,
              "Collections": [
                {
                  "Id": "6a1b6a8e-6b8a-4b8a-9b8a-6b8a4b8a9b8b",
                  "Name": "Pets",
                  "Requests": [
                    { "Id": "11111111-1111-1111-1111-111111111111", "Name": "First" },
                    { "Id": "22222222-2222-2222-2222-222222222222", "Name": "Second" },
                    { "Id": "33333333-3333-3333-3333-333333333333", "Name": "Third" }
                  ]
                }
              ]
            }
            """);
        var service = new WorkspaceService(_tempDirectory);

        var loaded = service.LoadWorkspace();

        Assert.Equal(Workspace.CurrentSchemaVersion, loaded.SchemaVersion);
        var requests = loaded.Collections[0].Requests;
        Assert.Equal(["First", "Second", "Third"], requests.Select(r => r.Name));
        Assert.Equal([0, 1, 2], requests.Select(r => r.SortOrder));
        Assert.Empty(loaded.Collections[0].Folders);
    }
}
