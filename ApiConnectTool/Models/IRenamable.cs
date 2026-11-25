namespace ApiConnectTool.Models;

/// <summary>Shared shape for sidebar-renamable entities (collections, folders, requests) so
/// rename-commit/cancel logic can live once in the view model instead of switching over three
/// parallel model types. Implementers keep their own [JsonIgnore] on IsEditing/RenameOriginalName -
/// attributes on the interface members would not apply to their serialization.</summary>
public interface IRenamable
{
    string Name { get; set; }
    bool IsEditing { get; set; }
    string? RenameOriginalName { get; set; }
}
