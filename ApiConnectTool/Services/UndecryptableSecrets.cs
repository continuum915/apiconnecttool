using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public enum UndecryptableSecretLocationKind
{
    RequestAuth,
    CollectionAuth,
    FolderAuth,
    Environment,
    CollectionVariable,
    GlobalVariable,
    CollectionEnvironment
}

public enum UndecryptableSecretField
{
    Token,
    Password,
    ClientSecret,
    InitialValue,
    CurrentValue
}

/// <summary>Where a secret that could not be decrypted lives. Carries display names and field
/// identity only - never the value or its stored payload.</summary>
/// <param name="OwnerName">Display name of the owning request, collection, folder, or environment;
/// empty for global variables.</param>
/// <param name="VariableKey">The variable's key, for variable locations.</param>
/// <param name="CollectionName">The owning collection, for collection environment locations.</param>
public sealed record UndecryptableSecretLocation(
    UndecryptableSecretLocationKind Kind,
    string OwnerName,
    UndecryptableSecretField Field,
    string? VariableKey = null,
    string? CollectionName = null);

internal static class UndecryptableSecretScanner
{
    public static IReadOnlyList<UndecryptableSecretLocation> Scan(Workspace workspace)
    {
        var found = new List<UndecryptableSecretLocation>();
        foreach (var collection in workspace.Collections ?? [])
        {
            AddAuth(found, UndecryptableSecretLocationKind.CollectionAuth, collection.Name, collection.Auth);
            AddVariables(found, UndecryptableSecretLocationKind.CollectionVariable, collection.Name, collection.Variables);
            foreach (var environment in collection.Environments ?? [])
            {
                AddVariables(found, UndecryptableSecretLocationKind.CollectionEnvironment, environment.Name, environment.Variables, collection.Name);
            }

            foreach (var folder in collection.Folders ?? [])
            {
                AddAuth(found, UndecryptableSecretLocationKind.FolderAuth, folder.Name, folder.Auth);
            }

            foreach (var request in collection.Requests ?? [])
            {
                AddAuth(found, UndecryptableSecretLocationKind.RequestAuth, request.Name, request.Auth);
            }
        }

        foreach (var environment in workspace.Environments ?? [])
        {
            AddVariables(found, UndecryptableSecretLocationKind.Environment, environment.Name, environment.Variables);
        }

        AddVariables(found, UndecryptableSecretLocationKind.GlobalVariable, string.Empty, workspace.GlobalVariables);
        return found;
    }

    private static void AddAuth(List<UndecryptableSecretLocation> found, UndecryptableSecretLocationKind kind, string owner, RequestAuth? auth)
    {
        if (auth is null) return;
        if (auth.Token.FailedToDecrypt) found.Add(new(kind, owner, UndecryptableSecretField.Token));
        if (auth.Password.FailedToDecrypt) found.Add(new(kind, owner, UndecryptableSecretField.Password));
        if (auth.ClientSecret.FailedToDecrypt) found.Add(new(kind, owner, UndecryptableSecretField.ClientSecret));
    }

    private static void AddVariables(List<UndecryptableSecretLocation> found, UndecryptableSecretLocationKind kind, string owner,
        IEnumerable<EnvironmentVariable>? variables, string? collectionName = null)
    {
        foreach (var variable in variables ?? [])
        {
            if (variable.InitialValue.FailedToDecrypt) found.Add(new(kind, owner, UndecryptableSecretField.InitialValue, variable.Key, collectionName));
            if (variable.CurrentValue.FailedToDecrypt) found.Add(new(kind, owner, UndecryptableSecretField.CurrentValue, variable.Key, collectionName));
        }
    }
}
