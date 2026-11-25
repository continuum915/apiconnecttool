using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

/// <summary>One request in the runner's "Requests to run" checklist.</summary>
public sealed partial class RunRequestChoice(RequestItem request, string? folderName) : ObservableObject
{
    [ObservableProperty] private bool _isIncluded = true;

    public RequestItem Request { get; } = request;
    public string Name => Request.Name;
    public string Method => Request.Method.Method;
    /// <summary>The request's folder, or null for a request at the collection root.</summary>
    public string? FolderName { get; } = folderName;
}
