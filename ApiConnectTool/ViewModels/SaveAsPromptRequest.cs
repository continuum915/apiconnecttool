using System.Collections.Generic;

namespace ApiConnectTool.ViewModels;

public enum SaveAsPromptMode
{
    /// <summary>First save of a request that isn't in any collection yet.</summary>
    SaveNew,
    /// <summary>Save Request As: a new independent copy of the active tab.</summary>
    SaveAsCopy,
    /// <summary>Sidebar Duplicate: the copy always lands next to the original, so no location.</summary>
    Duplicate
}

/// <summary>What the Save / Save As / Duplicate naming dialog should ask for.</summary>
public sealed record SaveAsPromptRequest(
    SaveAsPromptMode Mode,
    string DefaultName,
    IReadOnlyList<SaveDestination> Destinations,
    SaveDestination? DefaultDestination);
