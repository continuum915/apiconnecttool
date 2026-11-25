using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Models;

public partial class CollectionFolder : ObservableObject, IRenamable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty, JsonIgnore] private bool _isEditing;
    [JsonIgnore] public string? RenameOriginalName { get; set; }

    // null = top-level in the collection.
    public Guid? ParentFolderId { get; set; }
    public int SortOrder { get; set; }
    [ObservableProperty] private RequestAuth _auth = new() { Type = AuthType.Inherit };
}
