using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiConnectTool.Models;

public partial class RequestCollection : ObservableObject, IRenamable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty, JsonIgnore] private bool _isEditing;
    [JsonIgnore] public string? RenameOriginalName { get; set; }
    public ObservableCollection<CollectionFolder> Folders { get; set; } = new();
    public ObservableCollection<RequestItem> Requests { get; set; } = new();
    public ObservableCollection<EnvironmentVariable> Variables { get; set; } = new();

    /// <summary>Environments owned by this collection, in display order. IDs are unique across the
    /// whole workspace, including global environments.</summary>
    public ObservableCollection<RequestEnvironment> Environments { get; set; } = new();

    /// <summary>The selected entry of <see cref="Environments"/>, or null for none. Independent of
    /// every other collection and of the workspace's active global environment.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveEnvironment))] private Guid? _activeEnvironmentId;

    /// <summary>The environment <see cref="ActiveEnvironmentId"/> selects, or null when none is
    /// selected or the ID matches none of <see cref="Environments"/>.</summary>
    [JsonIgnore]
    public RequestEnvironment? ActiveEnvironment =>
        ActiveEnvironmentId is { } id ? Environments.FirstOrDefault(environment => environment.Id == id) : null;
    [ObservableProperty] private RequestAuth _auth = new() { AllowInheritance = false };
    partial void OnAuthChanging(RequestAuth value)
    {
        if (value.Type == AuthType.Inherit) throw new ArgumentException("Collection authorization cannot inherit.", nameof(value));
        value.AllowInheritance = false;
    }
}
