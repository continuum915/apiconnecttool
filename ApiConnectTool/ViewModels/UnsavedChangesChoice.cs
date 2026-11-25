namespace ApiConnectTool.ViewModels;

/// <summary>Answer to the unsaved-changes prompt shown before a dirty tab is closed or the app
/// exits with dirty tabs.</summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}
