using System;
using System.IO;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class UndecryptableSecretsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private string WorkspacePath => Path.Combine(_tempDirectory, "workspace.json");
    private string BackupPath => Path.Combine(_tempDirectory, "workspace.json.bak");

    private const string WorkspaceJson = """
        {
          "SchemaVersion": 10,
          "Collections": [
            {
              "Name": "Pets",
              "Auth": { "Type": 1, "Token": "ENCRYPTED:opaque-collection-token" },
              "Variables": [ { "Key": "apiKey", "InitialValue": "ENCRYPTED:opaque-collection-variable" } ],
              "Folders": [ { "Id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Name": "Admin", "Auth": { "Type": 2, "Username": "u", "Password": "ENCRYPTED:opaque-folder-password" } } ],
              "Requests": [ { "Name": "List pets", "Auth": { "Type": 4, "ClientSecret": "ENCRYPTED:opaque-request-secret" } } ]
            }
          ],
          "Environments": [
            { "Name": "Staging", "Variables": [ { "Key": "password", "IsSecret": true, "CurrentValue": "ENCRYPTED:opaque-environment-override", "HasCurrentValue": true } ] }
          ],
          "GlobalVariables": [ { "Key": "token", "InitialValue": "ENCRYPTED:opaque-global" } ]
        }
        """;

    private static readonly string[] Payloads =
    [
        "ENCRYPTED:opaque-collection-token", "ENCRYPTED:opaque-collection-variable", "ENCRYPTED:opaque-folder-password",
        "ENCRYPTED:opaque-request-secret", "ENCRYPTED:opaque-environment-override", "ENCRYPTED:opaque-global"
    ];

    private WorkspaceService CreateServiceWithWorkspace()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(WorkspacePath, WorkspaceJson);
        return new WorkspaceService(new RefusingSecretStore(), _tempDirectory);
    }

    [Fact]
    public void UndecryptablePayloadsSurviveRepeatedSavesInMainFileAndBackup()
    {
        var service = CreateServiceWithWorkspace();
        var workspace = service.LoadWorkspace();

        service.SaveWorkspace(workspace);
        service.SaveWorkspace(workspace);

        var main = File.ReadAllText(WorkspacePath);
        var backup = File.ReadAllText(BackupPath);
        foreach (var payload in Payloads)
        {
            Assert.Contains(payload, main);
            Assert.Contains(payload, backup);
        }
    }

    [Fact]
    public void UndecryptableValueStaysUnusableAfterReload()
    {
        var service = CreateServiceWithWorkspace();
        service.SaveWorkspace(service.LoadWorkspace());

        var reloaded = service.LoadWorkspace();

        var secret = reloaded.Collections[0].Requests[0].Auth.ClientSecret;
        Assert.True(secret.FailedToDecrypt);
        Assert.Equal(string.Empty, secret.Value);
        Assert.Equal(string.Empty, secret.ToString());
    }

    [Fact]
    public void AssigningNewValueReplacesPreservedPayload()
    {
        var service = CreateServiceWithWorkspace();
        var workspace = service.LoadWorkspace();

        workspace.Collections[0].Requests[0].Auth.ClientSecretText = "fresh-secret";
        workspace.GlobalVariables[0].InitialValue = string.Empty;
        service.SaveWorkspace(workspace);
        service.SaveWorkspace(workspace);

        var main = File.ReadAllText(WorkspacePath);
        var backup = File.ReadAllText(BackupPath);
        Assert.DoesNotContain("opaque-request-secret", main);
        Assert.DoesNotContain("opaque-request-secret", backup);
        Assert.Contains("ENCRYPTED:protected:fresh-secret", main);
        Assert.DoesNotContain("opaque-global", main);
        Assert.Contains("opaque-folder-password", main);
        Assert.False(workspace.Collections[0].Requests[0].Auth.ClientSecret.FailedToDecrypt);
    }

    [Fact]
    public void LoadReportsEachUndecryptableSecretLocationWithoutValues()
    {
        var service = CreateServiceWithWorkspace();

        service.LoadWorkspace();

        Assert.Equal(
        [
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.CollectionAuth, "Pets", UndecryptableSecretField.Token),
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.CollectionVariable, "Pets", UndecryptableSecretField.InitialValue, "apiKey"),
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.FolderAuth, "Admin", UndecryptableSecretField.Password),
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.RequestAuth, "List pets", UndecryptableSecretField.ClientSecret),
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.Environment, "Staging", UndecryptableSecretField.CurrentValue, "password"),
            new UndecryptableSecretLocation(UndecryptableSecretLocationKind.GlobalVariable, string.Empty, UndecryptableSecretField.InitialValue, "token"),
        ], service.UndecryptableSecrets);
        Assert.DoesNotContain(service.UndecryptableSecrets, location => location.ToString().Contains("opaque"));
    }

    [Fact]
    public void LoadOfReadableWorkspaceReportsNothing()
    {
        Directory.CreateDirectory(_tempDirectory);
        var service = new WorkspaceService(new RefusingSecretStore(), _tempDirectory);
        var workspace = new Workspace();
        workspace.GlobalVariables.Add(new() { Key = "plain", InitialValue = "" });
        service.SaveWorkspace(workspace);

        service.LoadWorkspace();

        Assert.Empty(service.UndecryptableSecrets);
    }

    [Fact]
    public void UndecryptableLegacyTokenMovesToAuthSoTabSavesKeepIt()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(WorkspacePath,
            """{"SchemaVersion":1,"Collections":[{"Requests":[{"Name":"Old","AuthToken":"ENCRYPTED:opaque-legacy"}]}]}""");
        var service = new WorkspaceService(new RefusingSecretStore(), _tempDirectory);
        var workspace = service.LoadWorkspace();
        var collection = workspace.Collections[0];

        collection.Requests[0] = collection.Requests[0].CloneForEditing();
        service.SaveWorkspace(workspace);

        Assert.Equal(AuthType.Bearer, collection.Requests[0].Auth.Type);
        Assert.Contains("ENCRYPTED:opaque-legacy", File.ReadAllText(WorkspacePath));
        Assert.Equal([new UndecryptableSecretLocation(UndecryptableSecretLocationKind.RequestAuth, "Old", UndecryptableSecretField.Token)],
            service.UndecryptableSecrets);
    }

    [Fact]
    public void WritingBackTheSameEmptyTextKeepsPayload()
    {
        var service = CreateServiceWithWorkspace();
        var workspace = service.LoadWorkspace();

        workspace.Collections[0].Requests[0].Auth.ClientSecretText = string.Empty;
        workspace.Collections[0].Auth.TokenText = string.Empty;
        workspace.Collections[0].Folders[0].Auth.PasswordText = string.Empty;
        workspace.GlobalVariables[0].InitialValueText = string.Empty;
        service.SaveWorkspace(workspace);

        var main = File.ReadAllText(WorkspacePath);
        foreach (var payload in Payloads) Assert.Contains(payload, main);
    }

    [Fact]
    public void ClonedAuthKeepsPayloadSoSavingAnEditedCopyDoesNotWipeIt()
    {
        var service = CreateServiceWithWorkspace();
        var workspace = service.LoadWorkspace();
        var request = workspace.Collections[0].Requests[0];

        request.Auth = request.Auth.Clone();
        service.SaveWorkspace(workspace);

        Assert.Contains("ENCRYPTED:opaque-request-secret", File.ReadAllText(WorkspacePath));
    }

    [Fact]
    public async System.Threading.Tasks.Task HistoryNeverWritesPreservedPayload()
    {
        var service = CreateServiceWithWorkspace();
        var request = service.LoadWorkspace().Collections[0].Requests[0];
        var history = new HistoryStore(new RefusingSecretStore(), _tempDirectory);

        history.Add(new HistoryEntry { RequestSnapshot = request.Clone() });
        await history.FlushAsync();

        Assert.DoesNotContain("opaque", File.ReadAllText(Path.Combine(_tempDirectory, "history.json")));
    }

    [Fact]
    public void ExportNeverWritesPreservedPayload()
    {
        var service = CreateServiceWithWorkspace();
        var collection = service.LoadWorkspace().Collections[0];

        var exported = new CollectionExportService().Export(collection, Guid.NewGuid(), includeSecrets: true);

        Assert.DoesNotContain("opaque", exported);
    }

    /// <summary>Protects reversibly, but refuses every decryption - stands in for a workspace moved
    /// to another machine or Windows profile.</summary>
    private sealed class RefusingSecretStore : ISecretStore
    {
        public bool TryProtect(string plaintext, out string? protectedPayload)
        {
            protectedPayload = "protected:" + plaintext;
            return true;
        }

        public bool TryUnprotect(string protectedPayload, out string? plaintext)
        {
            plaintext = null;
            return false;
        }
    }
}
