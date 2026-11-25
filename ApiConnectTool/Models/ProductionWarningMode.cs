namespace ApiConnectTool.Models;

/// <summary>Controls how often RequestTabViewModel.SendAsync prompts for confirmation before
/// sending against an environment marked RequestEnvironment.IsProduction.</summary>
public enum ProductionWarningMode
{
    AlwaysWarn,
    WarnOncePerSession,
    NeverWarn
}
