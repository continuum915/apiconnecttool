namespace ApiConnectTool.ViewModels;

/// <summary>Result of the "Save Request As" naming dialog. Null (rather than an instance of this
/// type) signals the user cancelled. A null <paramref name="Destination"/> means "use the
/// request's default location".</summary>
public sealed record SaveAsPromptResult(string Name, bool DontShowAgain, SaveDestination? Destination = null);
