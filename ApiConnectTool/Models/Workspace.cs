using System;
using System.Collections.ObjectModel;

namespace ApiConnectTool.Models;

public class Workspace
{
    public const int CurrentSchemaVersion = 11;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ObservableCollection<RequestCollection> Collections { get; set; } = new();
    public ObservableCollection<RequestEnvironment> Environments { get; set; } = new();
    public ObservableCollection<EnvironmentVariable> GlobalVariables { get; set; } = new();
    public Guid? SelectedEnvironmentId { get; set; }
    public ProductionWarningMode ProductionWarningMode { get; set; } = ProductionWarningMode.AlwaysWarn;

    public AppTheme Theme { get; set; } = AppTheme.Win7Light;

    /// <summary>When true, "Save Request As" and sidebar "Duplicate" skip the naming
    /// confirmation dialog and immediately create a copy named "Copy of &lt;original&gt;". Set from
    /// that dialog's "Don't show this again" checkbox; re-enabled from Preferences.</summary>
    public bool SkipSaveAsConfirmation { get; set; }
}
